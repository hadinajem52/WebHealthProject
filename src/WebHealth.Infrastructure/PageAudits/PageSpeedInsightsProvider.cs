using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WebHealth.Application.PageAudits;
using WebHealth.Domain.PageAudits;

namespace WebHealth.Infrastructure.PageAudits;

/// <summary>
/// The Google PageSpeed Insights adapter.
/// </summary>
/// <remarks>
/// <para>
/// This does not use <c>SafeHttpTransport</c>, and the difference is not incidental. That
/// transport exists to contact user-configured monitored targets under DNS, redirect, concurrency
/// and SSRF rules, because the URL comes from a user. Here the URL is one fixed Google API host
/// and the monitored URL travels as a query value, so the SSRF surface is a query parameter rather
/// than a destination. Reusing the transport would apply the wrong protections to the wrong risk.
/// </para>
/// <para>
/// The API key travels in the query string, because that is the only way this API accepts it.
/// Everything else in this class follows from that: the request URI is never logged, never
/// persisted, never attached to an exception, and never included in a diagnostic.
/// </para>
/// </remarks>
internal sealed class PageSpeedInsightsProvider(
    IHttpClientFactory httpClientFactory,
    PageSpeedInsightsOptions options,
    ILogger<PageSpeedInsightsProvider> logger) : IPageAuditProvider
{
    /// <summary>
    /// Constants, not configuration. A settings file that could move the host would turn this
    /// into a general outbound HTTP client with our API key attached.
    /// </summary>
    public const string ServiceOrigin = "https://pagespeedonline.googleapis.com/";

    public const string RunPagespeedPath = "pagespeedonline/v5/runPagespeed";

    public string ProviderName => PageAuditProviders.PageSpeedInsights;

    public async Task<PageAuditProviderResult> RunAsync(
        PageAuditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!options.HasApiKey)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderAuthenticationFailed,
                "No PageSpeed Insights API key is configured.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RequestTimeout);

        try
        {
            return await SendAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.Cancelled,
                "The audit was cancelled before the provider answered.");
        }
        catch (OperationCanceledException)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderTimeout,
                $"The provider did not answer within {options.RequestTimeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException exception)
        {
            // Neither the message nor the exception itself is carried. An HttpRequestException
            // raised while sending can name the request URI, and the request URI carries the key;
            // attaching it as an inner exception would put that text inside anything that later
            // called ToString() - a Hangfire job record, for one. Only the type name survives.
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderUnavailable,
                $"The provider could not be reached ({exception.GetType().Name}).");
        }
        catch (JsonException)
        {
            // Same reasoning: a JSON exception quotes the payload it choked on.
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderResponseInvalid,
                "The provider response was not valid JSON.");
        }
    }

    private async Task<PageAuditProviderResult> SendAsync(
        PageAuditRequest request,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(PageSpeedInsightsOptions.ClientName);
        using var message = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(request));

        using var response = await client.SendAsync(
            message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw FailureFor(response);
        }

        using var document = await ReadBoundedJsonAsync(response, cancellationToken);
        var reader = new PageAuditResponseReader(options);
        var result = reader.Read(document, request.TargetUrl.ToString());

        // No URI, no key, no response body. Everything here is either ours or a bounded provider
        // fact, and there is a regression test over the recorded log to keep it that way.
        logger.LogInformation(
            "PageSpeed audit completed. Provider={Provider} Strategy={Strategy} "
            + "LighthouseVersion={LighthouseVersion} AuditItemCount={AuditItemCount}",
            ProviderName,
            request.Strategy,
            result.LighthouseVersion,
            result.Items.Count);

        return result;
    }

    /// <summary>
    /// Built with <see cref="UriBuilder" /> and an escaped query, so the audited URL is encoded
    /// exactly once. Concatenating it would let a query string in the target truncate ours, and
    /// the parameter it would most easily displace is the category.
    /// </summary>
    private Uri BuildRequestUri(PageAuditRequest request)
    {
        var query = string.Join('&',
        [
            $"url={Uri.EscapeDataString(request.TargetUrl.ToString())}",
            $"category={Uri.EscapeDataString(PageAuditCategories.SeoParameter)}",
            $"strategy={Uri.EscapeDataString(PageAuditStrategies.ToParameter(request.Strategy))}",
            $"locale={Uri.EscapeDataString(request.Locale)}",
            $"key={Uri.EscapeDataString(options.ApiKey!)}"
        ]);

        return new UriBuilder(new Uri(new Uri(ServiceOrigin), RunPagespeedPath))
        {
            Query = query
        }.Uri;
    }

    /// <summary>
    /// Reads the body under a byte cap applied before deserialization. A declared content length
    /// over the cap is refused without reading anything; a response that lies about its length or
    /// omits one is refused as it crosses the cap.
    /// </summary>
    private async Task<JsonDocument> ReadBoundedJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is { } declared
            && declared > options.MaximumResponseBytes)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderResponseTooLarge,
                $"The provider response declared {declared} bytes, above the configured ceiling "
                + $"of {options.MaximumResponseBytes}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > options.MaximumResponseBytes)
            {
                throw new PageAuditProviderException(
                    PageAuditFailureCategories.ProviderResponseTooLarge,
                    $"The provider response exceeded the configured ceiling of "
                    + $"{options.MaximumResponseBytes} bytes.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        buffer.Position = 0;

        // A bounded depth as well as a bounded length: a well-formed response nested thousands of
        // levels deep is small on the wire and unbounded to parse.
        return await JsonDocument.ParseAsync(
            buffer,
            new JsonDocumentOptions { MaxDepth = 64 },
            cancellationToken);
    }

    /// <summary>
    /// The HTTP status, as a normalized reason. Google's own error body is never read: it can
    /// echo the request URI, and the request URI carries the key.
    /// </summary>
    private static PageAuditProviderException FailureFor(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        return response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => new(
                PageAuditFailureCategories.ProviderRateLimited,
                "The provider rate-limited the request.",
                RetryAfterOf(response)),

            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(
                PageAuditFailureCategories.ProviderAuthenticationFailed,
                $"The provider refused the API key with HTTP {status}. Check that the key is "
                + "valid and that its API restriction allows PageSpeed Insights."),

            // A 400 here is the provider judging the target, not the request: the query is built
            // from constants and one stored URL, so the shape is ours and correct by construction.
            HttpStatusCode.BadRequest => new(
                PageAuditFailureCategories.TargetRejected,
                "The provider rejected the target URL. It may be unreachable from the public "
                + "internet, or may not be a page it can audit."),

            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => new(
                PageAuditFailureCategories.ProviderTimeout,
                $"The provider timed out with HTTP {status}."),

            HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable => new(
                PageAuditFailureCategories.ProviderUnavailable,
                $"The provider is unavailable: HTTP {status}.",
                RetryAfterOf(response)),

            _ => new(
                PageAuditFailureCategories.UnknownProviderFailure,
                $"The provider answered with an unexpected HTTP {status}.")
        };
    }

    /// <summary>
    /// The provider's own <c>Retry-After</c>, honoured only when it is present and inside a delay
    /// a worker may reasonably hold. An hour-long value would occupy the single audit worker doing
    /// nothing, so an unusable one is ignored rather than obeyed.
    /// </summary>
    private static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return delay is { } value && value > TimeSpan.Zero && value <= TimeSpan.FromMinutes(10)
            ? value
            : null;
    }
}
