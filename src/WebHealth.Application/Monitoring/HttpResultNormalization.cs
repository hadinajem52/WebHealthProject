using System.Text;
using WebHealth.Domain.Normalization;

namespace WebHealth.Application.Monitoring;

public static class HttpResultOutcomes
{
    public const string Healthy = "Healthy";
    public const string Warning = "Warning";
    public const string Critical = "Critical";
    public const string Cancelled = "Cancelled";
}

public static class HttpFailureCategories
{
    public const string Dns = "Dns";
    public const string Connection = "Connection";
    public const string Tls = "Tls";
    public const string Timeout = "Timeout";
    public const string Cancellation = "Cancellation";
    public const string ClientError = "ClientError";
    public const string ServerError = "ServerError";
    public const string RedirectLoop = "RedirectLoop";
    public const string ExcessiveRedirects = "ExcessiveRedirects";
    public const string ContentMismatch = "ContentMismatch";
    public const string ResponseTooLarge = "ResponseTooLarge";
    public const string HttpsRequired = "HttpsRequired";
    public const string InvalidConfiguration = "InvalidConfiguration";
    public const string DestinationPolicy = "DestinationPolicy";
    public const string InvalidRedirect = "InvalidRedirect";
    public const string ExecutionExhausted = "ExecutionExhausted";
    public const string TargetIneligible = "TargetIneligible";
    public const string Protocol = "Protocol";
}

public static class FindingSeverities
{
    public const string Warning = "Warning";
    public const string Critical = "Critical";
}

public sealed record HttpResultPolicy(
    IReadOnlyCollection<int> AcceptedStatusCodes,
    string? RequiredContentMarker,
    bool IsContentMarkerCaseSensitive,
    string ProductionHttpSeverity,
    int MaxResponseBodyBytes)
{
    public static HttpResultPolicy Default { get; } = new(
        [], null, false, FindingSeverities.Warning,
        SafeHttpTransportDefaults.MaxDecodedBodyBytes);
}

public sealed record NormalizeHttpResult(
    SafeHttpTransportRequest Request,
    SafeHttpTransportResult Transport,
    HttpResultPolicy Policy,
    DateTimeOffset MeasuredAt);

public sealed record NormalizedHttpResult(
    string Outcome,
    string? FailureCategory,
    int? HttpStatus,
    int TotalDurationMs,
    long? DecodedLength,
    string? LengthSource,
    string MonitorSource,
    DateTimeOffset MeasuredAt,
    string? SafeDiagnostic,
    IReadOnlyList<NormalizedRedirectHop> Redirects,
    IReadOnlyList<NormalizedFinding> Findings);

public sealed record NormalizedRedirectHop(
    int HopNumber,
    string FromUrl,
    string ToUrl,
    int HttpStatus,
    bool IsLoop);

public sealed record NormalizedFinding(
    string FailureCategory,
    string RuleKey,
    string Severity,
    string? ObservedValue,
    string? ExpectedValue,
    string IssueKey);

public static class HttpResultNormalizer
{
    public static NormalizedHttpResult Normalize(NormalizeHttpResult input)
    {
        Validate(input.Policy);
        var redirects = input.Transport.Redirects.Select((hop, index) => new NormalizedRedirectHop(
            index + 1, hop.FromUrl, hop.ToUrl, hop.StatusCode, hop.IsLoop)).ToArray();
        var findings = Evaluate(input).ToArray();
        var category = SelectFailureCategory(input.Transport, findings);
        return new(
            SelectOutcome(input.Transport, findings),
            category,
            input.Transport.StatusCode,
            ToBoundedMilliseconds(input.Transport.Duration),
            input.Transport.Failure is not null ? null : input.Transport.ResponseBytesRead,
            input.Transport.Failure is not null
                ? null
                : input.Transport.BodyTruncated ? "BoundedDecoded" : "MeasuredDecoded",
            "WebHealthSafeHttpV1",
            input.MeasuredAt,
            Diagnostic(category),
            redirects,
            findings);
    }

    private static IEnumerable<NormalizedFinding> Evaluate(NormalizeHttpResult input)
    {
        if (input.Transport.Failure is { } transportFailure)
        {
            if (transportFailure != SafeHttpFailureKind.Cancelled)
            {
                yield return FailureFinding(MapTransportFailure(transportFailure));
            }
            yield break;
        }

        if (input.Transport.BodyTruncated)
        {
            yield return Finding(
                HttpFailureCategories.ResponseTooLarge,
                $">{input.Policy.MaxResponseBodyBytes} decoded bytes",
                $"<={input.Policy.MaxResponseBodyBytes} decoded bytes");
        }

        var statusFinding = EvaluateStatus(input.Transport.StatusCode, input.Policy.AcceptedStatusCodes);
        if (statusFinding is not null)
        {
            yield return statusFinding;
        }

        if (RequiresHttpsFinding(input))
        {
            yield return Finding(
                HttpFailureCategories.HttpsRequired,
                "Final destination uses HTTP",
                "Final destination uses HTTPS",
                input.Policy.ProductionHttpSeverity);
        }

        if (!input.Transport.BodyTruncated
            && statusFinding is null
            && !ContainsRequiredMarker(input.Transport.Body.Span, input.Policy))
        {
            yield return Finding(
                HttpFailureCategories.ContentMismatch,
                "Required marker was not found",
                "Configured marker is present");
        }
    }

    private static NormalizedFinding? EvaluateStatus(
        int? status,
        IReadOnlyCollection<int> acceptedStatuses)
    {
        if (status is null)
        {
            return FailureFinding(HttpFailureCategories.Protocol);
        }

        if (status is >= 500 and <= 599)
        {
            return Finding(HttpFailureCategories.ServerError, status.ToString(), "Status below 500");
        }

        if (status is >= 200 and <= 299 || acceptedStatuses.Contains(status.Value))
        {
            return null;
        }

        return status is >= 400 and <= 499
            ? Finding(HttpFailureCategories.ClientError, status.ToString(), "Accepted HTTP status")
            : FailureFinding(HttpFailureCategories.Protocol);
    }

    private static bool RequiresHttpsFinding(NormalizeHttpResult input)
    {
        if (!input.Request.IsProduction)
        {
            return false;
        }

        var start = EndpointUrlNormalizer.Normalize(input.Request.Url);
        var final = input.Transport.FinalDestination is null
            ? null
            : EndpointUrlNormalizer.Normalize(input.Transport.FinalDestination.Url);
        return start.Succeeded
            && new Uri(start.NormalizedUrl!, UriKind.Absolute).Scheme == Uri.UriSchemeHttp
            && (final is null
                || !final.Succeeded
                || new Uri(final.NormalizedUrl!, UriKind.Absolute).Scheme != Uri.UriSchemeHttps);
    }

    private static bool ContainsRequiredMarker(ReadOnlySpan<byte> body, HttpResultPolicy policy)
    {
        if (string.IsNullOrEmpty(policy.RequiredContentMarker))
        {
            return true;
        }

        var comparison = policy.IsContentMarkerCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return Encoding.UTF8.GetString(body).Contains(policy.RequiredContentMarker, comparison);
    }

    private static string? SelectFailureCategory(
        SafeHttpTransportResult transport,
        IReadOnlyList<NormalizedFinding> findings)
    {
        if (transport.Failure is { } failure)
        {
            return MapTransportFailure(failure);
        }

        return findings
            .OrderByDescending(FailurePriority)
            .ThenBy(finding => finding.RuleKey, StringComparer.Ordinal)
            .Select(finding => finding.FailureCategory)
            .FirstOrDefault();
    }

    private static int FailurePriority(NormalizedFinding finding) =>
        (finding.Severity, finding.FailureCategory) switch
        {
            (FindingSeverities.Critical, HttpFailureCategories.ResponseTooLarge) => 500,
            (FindingSeverities.Critical, HttpFailureCategories.ServerError) => 490,
            (FindingSeverities.Critical, HttpFailureCategories.ClientError) => 480,
            (FindingSeverities.Critical, HttpFailureCategories.ContentMismatch) => 470,
            (FindingSeverities.Critical, _) => 400,
            (FindingSeverities.Warning, HttpFailureCategories.HttpsRequired) => 300,
            (FindingSeverities.Warning, _) => 200,
            _ => 0
        };

    private static string SelectOutcome(
        SafeHttpTransportResult transport,
        IReadOnlyList<NormalizedFinding> findings)
    {
        if (transport.Failure == SafeHttpFailureKind.Cancelled)
        {
            return HttpResultOutcomes.Cancelled;
        }

        if (findings.Any(finding => finding.Severity == FindingSeverities.Critical))
        {
            return HttpResultOutcomes.Critical;
        }

        return findings.Count > 0 ? HttpResultOutcomes.Warning : HttpResultOutcomes.Healthy;
    }

    private static string MapTransportFailure(SafeHttpFailureKind failure) => failure switch
    {
        SafeHttpFailureKind.NameResolution => HttpFailureCategories.Dns,
        SafeHttpFailureKind.Connection => HttpFailureCategories.Connection,
        SafeHttpFailureKind.Tls => HttpFailureCategories.Tls,
        SafeHttpFailureKind.Timeout => HttpFailureCategories.Timeout,
        SafeHttpFailureKind.Cancelled => HttpFailureCategories.Cancellation,
        SafeHttpFailureKind.RedirectLoop => HttpFailureCategories.RedirectLoop,
        SafeHttpFailureKind.RedirectLimit => HttpFailureCategories.ExcessiveRedirects,
        SafeHttpFailureKind.ResponseHeadersTooLarge => HttpFailureCategories.ResponseTooLarge,
        SafeHttpFailureKind.InvalidUrl => HttpFailureCategories.InvalidConfiguration,
        SafeHttpFailureKind.TargetNotAuthorized or SafeHttpFailureKind.DestinationRejected =>
            HttpFailureCategories.DestinationPolicy,
        SafeHttpFailureKind.RedirectMissingLocation or SafeHttpFailureKind.RedirectInvalid =>
            HttpFailureCategories.InvalidRedirect,
        SafeHttpFailureKind.HttpsDowngrade => HttpFailureCategories.HttpsRequired,
        _ => HttpFailureCategories.Protocol
    };

    private static NormalizedFinding FailureFinding(string category) =>
        Finding(category, category, "Successful HTTP check");

    private static NormalizedFinding Finding(
        string category,
        string? observed,
        string? expected,
        string severity = FindingSeverities.Critical) =>
        new(
            category,
            $"Http.{category}",
            severity,
            observed,
            expected,
            HttpIssueIdentity.Create($"Http.{category}"));

    private static string? Diagnostic(string? category) => category switch
    {
        null => null,
        HttpFailureCategories.Dns => "DNS resolution failed.",
        HttpFailureCategories.Connection => "The connection failed.",
        HttpFailureCategories.Tls => "TLS validation or negotiation failed.",
        HttpFailureCategories.Timeout => "The check timed out.",
        HttpFailureCategories.Cancellation => "The check was cancelled.",
        HttpFailureCategories.RedirectLoop => "A redirect loop was detected.",
        HttpFailureCategories.ExcessiveRedirects => "The redirect limit was exceeded.",
        HttpFailureCategories.ContentMismatch => "Required content was not found.",
        HttpFailureCategories.ResponseTooLarge => "The response exceeded the read limit.",
        HttpFailureCategories.ClientError => "The response returned an unaccepted client status.",
        HttpFailureCategories.ServerError => "The response returned a server error.",
        HttpFailureCategories.HttpsRequired => "The production target did not finish on HTTPS.",
        HttpFailureCategories.DestinationPolicy => "The destination policy rejected the target.",
        HttpFailureCategories.InvalidConfiguration => "The target configuration is invalid.",
        HttpFailureCategories.InvalidRedirect => "The redirect target is invalid.",
        HttpFailureCategories.ExecutionExhausted => "The execution retry limit was exhausted.",
        HttpFailureCategories.TargetIneligible => "The target is not currently eligible for monitoring.",
        _ => "The HTTP exchange failed."
    };

    private static int ToBoundedMilliseconds(TimeSpan duration) =>
        (int)Math.Clamp(Math.Ceiling(duration.TotalMilliseconds), 0, int.MaxValue);

    private static void Validate(HttpResultPolicy policy)
    {
        if (policy.AcceptedStatusCodes.Any(status => status is < 100 or > 599)
            || policy.RequiredContentMarker?.Length > 500
            || policy.MaxResponseBodyBytes <= 0
            || policy.MaxResponseBodyBytes > SafeHttpTransportDefaults.MaxDecodedBodyBytes
            || policy.ProductionHttpSeverity is not (FindingSeverities.Warning or FindingSeverities.Critical))
        {
            throw new ArgumentException("The HTTP result policy is invalid.", nameof(policy));
        }
    }
}
