using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.Normalization;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class SafeHttpTransport(
    IHttpClientFactory httpClientFactory,
    IMonitoringTargetAuthorizer targetAuthorizer,
    SafeHttpConcurrencyLimiter concurrencyLimiter,
    SafeHttpTransportOptions options,
    TimeProvider timeProvider) : ISafeHttpTransport
{
    public async Task<SafeHttpTransportResult> SendAsync(
        SafeHttpTransportRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var redirects = new List<SafeHttpRedirectHop>();
        var normalized = EndpointUrlNormalizer.Normalize(request.Url);
        if (!normalized.Succeeded)
        {
            return Failure(SafeHttpFailureKind.InvalidUrl, stopwatch, redirects);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);

        try
        {
            using var globalLease = await concurrencyLimiter.AcquireGlobalAsync(timeout.Token);
            var current = new Uri(normalized.NormalizedUrl!, UriKind.Absolute);
            var visited = new HashSet<string>(StringComparer.Ordinal);

            while (true)
            {
                var currentNormalization = EndpointUrlNormalizer.Normalize(current.AbsoluteUri);
                if (!currentNormalization.Succeeded || !visited.Add(currentNormalization.NormalizedUrl!))
                {
                    return Failure(
                        currentNormalization.Succeeded
                            ? SafeHttpFailureKind.RedirectLoop
                            : SafeHttpFailureKind.RedirectInvalid,
                        stopwatch,
                        redirects);
                }

                if (!await targetAuthorizer.IsAuthorizedAsync(
                    request.EndpointId,
                    currentNormalization.NormalizedHost!,
                    currentNormalization.EffectivePort!.Value,
                    timeProvider.GetUtcNow(),
                    timeout.Token))
                {
                    return Failure(SafeHttpFailureKind.TargetNotAuthorized, stopwatch, redirects);
                }

                using var hostLease = await concurrencyLimiter.AcquireHostAsync(
                    currentNormalization.NormalizedHost!,
                    timeout.Token);
                using var message = new HttpRequestMessage(HttpMethod.Get, currentNormalization.NormalizedUrl!);
                message.Version = HttpVersion.Version11;
                message.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
                message.Headers.ConnectionClose = true;
                using var response = await httpClientFactory.CreateClient(SafeHttpTransportOptions.ClientName)
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if (!IsRedirect(response.StatusCode))
                {
                    var body = await ReadBodyAsync(response.Content, timeout.Token);
                    return new SafeHttpTransportResult(
                        null,
                        (int)response.StatusCode,
                        Destination(currentNormalization),
                        stopwatch.Elapsed,
                        body.BytesRead,
                        body.Truncated,
                        body.Content,
                        redirects);
                }

                if (response.Headers.Location is null)
                {
                    return Failure(SafeHttpFailureKind.RedirectMissingLocation, stopwatch, redirects);
                }

                if (redirects.Count >= options.MaxRedirects)
                {
                    return Failure(SafeHttpFailureKind.RedirectLimit, stopwatch, redirects);
                }

                Uri target;
                try
                {
                    target = new Uri(current, response.Headers.Location);
                }
                catch (UriFormatException)
                {
                    return Failure(SafeHttpFailureKind.RedirectInvalid, stopwatch, redirects);
                }

                var targetNormalization = EndpointUrlNormalizer.Normalize(target.AbsoluteUri);
                if (!targetNormalization.Succeeded)
                {
                    return Failure(SafeHttpFailureKind.RedirectInvalid, stopwatch, redirects);
                }

                if (request.IsProduction
                    && current.Scheme == Uri.UriSchemeHttps
                    && target.Scheme == Uri.UriSchemeHttp)
                {
                    return Failure(SafeHttpFailureKind.HttpsDowngrade, stopwatch, redirects);
                }

                redirects.Add(new SafeHttpRedirectHop(
                    (int)response.StatusCode,
                    current.Scheme,
                    currentNormalization.NormalizedHost!,
                    currentNormalization.EffectivePort!.Value,
                    target.Scheme,
                    targetNormalization.NormalizedHost!,
                    targetNormalization.EffectivePort!.Value));
                current = new Uri(targetNormalization.NormalizedUrl!, UriKind.Absolute);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(SafeHttpFailureKind.Cancelled, stopwatch, redirects);
        }
        catch (OperationCanceledException)
        {
            return Failure(SafeHttpFailureKind.Timeout, stopwatch, redirects);
        }
        catch (Exception exception) when (TryClassify(exception, out var failure))
        {
            return Failure(failure, stopwatch, redirects);
        }
    }

    private async Task<BodyReadResult> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[options.MaxDecodedBodyBytes + 1];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                break;
            }
            offset += read;
        }

        var truncated = offset > options.MaxDecodedBodyBytes;
        var length = Math.Min(offset, options.MaxDecodedBodyBytes);
        return new(buffer.AsMemory(0, length), offset, truncated);
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Redirect
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    private static SafeHttpDestination Destination(EndpointUrlNormalizationResult normalized) =>
        new(new Uri(normalized.NormalizedUrl!, UriKind.Absolute).Scheme,
            normalized.NormalizedHost!,
            normalized.EffectivePort!.Value);

    private static SafeHttpTransportResult Failure(
        SafeHttpFailureKind failure,
        Stopwatch stopwatch,
        IReadOnlyList<SafeHttpRedirectHop> redirects) =>
        new(failure, null, null, stopwatch.Elapsed, 0, false, ReadOnlyMemory<byte>.Empty, redirects);

    private static bool TryClassify(Exception exception, out SafeHttpFailureKind failure)
    {
        if (Find<SafeDestinationException>(exception) is not null)
        {
            failure = SafeHttpFailureKind.DestinationRejected;
            return true;
        }

        if (Find<AuthenticationException>(exception) is not null)
        {
            failure = SafeHttpFailureKind.Tls;
            return true;
        }

        if (Find<SocketException>(exception) is SocketException socketException)
        {
            failure = socketException.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData
                ? SafeHttpFailureKind.NameResolution
                : SafeHttpFailureKind.Connection;
            return true;
        }

        if (exception is HttpRequestException requestException)
        {
            failure = requestException.HttpRequestError switch
            {
                HttpRequestError.NameResolutionError => SafeHttpFailureKind.NameResolution,
                HttpRequestError.ConnectionError => SafeHttpFailureKind.Connection,
                HttpRequestError.SecureConnectionError => SafeHttpFailureKind.Tls,
                HttpRequestError.ConfigurationLimitExceeded => SafeHttpFailureKind.ResponseHeadersTooLarge,
                _ => SafeHttpFailureKind.Protocol
            };
            return true;
        }

        failure = default;
        return false;
    }

    private static TException? Find<TException>(Exception exception) where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException match)
            {
                return match;
            }
        }
        return null;
    }

    private sealed record BodyReadResult(ReadOnlyMemory<byte> Content, int BytesRead, bool Truncated);
}
