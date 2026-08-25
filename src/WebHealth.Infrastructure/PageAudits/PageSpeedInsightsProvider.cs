using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WebHealth.Application.PageAudits;
using WebHealth.Domain.PageAudits;

namespace WebHealth.Infrastructure.PageAudits;

internal sealed class PageSpeedInsightsProvider(
    IHttpClientFactory httpClientFactory,
    PageSpeedInsightsOptions options,
    ILogger<PageSpeedInsightsProvider> logger) : IPageAuditProvider
{
    public const string ServiceOrigin = "https://pagespeedonline.googleapis.com/";

    public const string RunPagespeedPath = "pagespeedonline/v5/runPagespeed";

    public string ProviderName => PageAuditProviders.PageSpeedInsights;

    public async Task<PageAuditProviderBatchResult> RunAsync(
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
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderUnavailable,
                $"The provider could not be reached ({exception.GetType().Name}).");
        }
        catch (JsonException)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderResponseInvalid,
                "The provider response was not valid JSON.");
        }
    }

    private async Task<PageAuditProviderBatchResult> SendAsync(
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
        var result = reader.Read(document, request.TargetUrl.ToString(), request.Categories);

        logger.LogInformation(
            "PageSpeed audit completed. Provider={Provider} Category={Category} Strategy={Strategy} "
            + "LighthouseVersion={LighthouseVersion} AuditItemCount={AuditItemCount}",
            ProviderName,
            string.Join(',', request.Categories),
            request.Strategy,
            result.Categories.Values.First().LighthouseVersion,
            result.Categories.Values.Sum(category => category.Items.Count));

        return result;
    }

    private Uri BuildRequestUri(PageAuditRequest request)
    {
        var parameters = new List<string>
        {
            $"url={Uri.EscapeDataString(request.TargetUrl.ToString())}",
            $"strategy={Uri.EscapeDataString(PageAuditStrategies.ToParameter(request.Strategy))}",
            $"locale={Uri.EscapeDataString(request.Locale)}",
            $"key={Uri.EscapeDataString(options.ApiKey!)}"
        };
        parameters.InsertRange(1, request.Categories.Select(category =>
            $"category={Uri.EscapeDataString(PageAuditCategories.ToParameter(category))}"));
        var query = string.Join('&', parameters);

        return new UriBuilder(new Uri(new Uri(ServiceOrigin), RunPagespeedPath))
        {
            Query = query
        }.Uri;
    }

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

        return await JsonDocument.ParseAsync(
            buffer,
            new JsonDocumentOptions { MaxDepth = 64 },
            cancellationToken);
    }

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
