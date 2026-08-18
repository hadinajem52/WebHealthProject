using System.Text;
using WebHealth.Domain.Monitoring;
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

    /// <summary>BR-P02: total response time breached its configured threshold.</summary>
    public const string SlowResponse = "SlowResponse";

    /// <summary>BR-P04: the page exceeded its size threshold.</summary>
    public const string PageTooLarge = "PageTooLarge";
}

public static class FindingSeverities
{
    public const string Warning = "Warning";

    /// <summary>
    /// Between warning and critical. It exists for rules whose urgency escalates while the
    /// endpoint itself is still serving traffic — certificate expiry inside 15 days (BR-C04) is
    /// the first of them.
    /// </summary>
    public const string High = "High";

    public const string Critical = "Critical";

    public static readonly string[] All = [Warning, High, Critical];

    public static int Rank(string severity) => severity switch
    {
        Critical => 3,
        High => 2,
        Warning => 1,
        _ => 0
    };

    /// <summary>Returns whichever of the two severities is more urgent.</summary>
    public static string Max(string first, string second) =>
        Rank(second) > Rank(first) ? second : first;

    /// <summary>
    /// Collapses a finding severity onto the three-state result outcome. <see cref="High" />
    /// maps to a <c>Warning</c> outcome deliberately: the outcome is the availability signal,
    /// and a certificate that expires in 15 days says nothing about the site being reachable
    /// today. The escalated urgency is carried by the finding and its incident instead.
    /// </summary>
    public static string ToOutcome(string severity) =>
        severity == Critical ? HttpResultOutcomes.Critical : HttpResultOutcomes.Warning;
}

/// <summary>
/// Rule keys for the performance rules (BR-P02, BR-P04). They are deliberately separate rule
/// keys — and therefore separate issue keys — from the availability rules, so a slow endpoint
/// and an unreachable endpoint track as independent incidents (BR-I04).
/// </summary>
public static class PerformanceRules
{
    public const string SlowResponse = "Http.SlowResponse";
    public const string PageTooLarge = "Http.PageTooLarge";

    /// <summary>
    /// BR-P03: three consecutive breaches before a slow-response incident opens, so one
    /// isolated slow sample never raises anything.
    /// </summary>
    public const int SlowResponseConfirmationCount = 3;

    /// <summary>
    /// The confirmation count a finding needs before it can open an incident. Slow response
    /// carries its own BR-P03 minimum; every other rule uses the monitor's own confirmation
    /// policy. The monitor's count still wins when it is the stricter of the two, because an
    /// operator who asked for five confirmations did not ask for three.
    /// </summary>
    public static int SelectFailureConfirmationCount(string ruleKey, int monitorConfirmationCount) =>
        ruleKey == SlowResponse
            ? Math.Max(monitorConfirmationCount, SlowResponseConfirmationCount)
            : monitorConfirmationCount;
}

/// <summary>
/// How a stored page size was measured (BR-P04). The label travels with the value so a report
/// never compares a compressed wire length against a decoded one without saying so.
/// </summary>
public static class PageLengthSources
{
    /// <summary>The response advertised a Content-Length: bytes as transferred, compression included.</summary>
    public const string TransferredContentLength = "TransferredContentLength";

    /// <summary>No Content-Length was advertised: decoded bytes actually read.</summary>
    public const string MeasuredDecoded = "MeasuredDecoded";

    /// <summary>The body hit the read cap, so the value is a lower bound, not a measurement.</summary>
    public const string BoundedDecoded = "BoundedDecoded";
}

public sealed record HttpResultPolicy(
    IReadOnlyCollection<int> AcceptedStatusCodes,
    string? RequiredContentMarker,
    bool IsContentMarkerCaseSensitive,
    string ProductionHttpSeverity,
    int MaxResponseBodyBytes,
    ResponseTimeThresholds? ResponseTime = null,
    long PageSizeWarningBytes = PerformanceEvaluation.DefaultPageSizeWarningBytes)
{
    /// <summary>
    /// The thresholds this result is judged against. They come from the check's configuration
    /// snapshot, so re-reading a stored result never re-judges it against a threshold that was
    /// changed afterwards (BR-P02).
    /// </summary>
    public ResponseTimeThresholds EffectiveResponseTime => ResponseTime ?? ResponseTimeThresholds.Default;

    public static HttpResultPolicy Default { get; } = new(
        [], null, false, FindingSeverities.Warning,
        SafeHttpTransportDefaults.MaxDecodedBodyBytes);
}

public sealed record NormalizeHttpResult(
    SafeHttpTransportRequest Request,
    SafeHttpTransportResult Transport,
    HttpResultPolicy Policy,
    DateTimeOffset MeasuredAt);

public sealed record NormalizedCheckResult(
    string Outcome,
    string? FailureCategory,
    int? HttpStatus,
    int TotalDurationMs,
    long? TransferredLength,
    long? DecodedLength,
    string? LengthSource,
    string MonitorSource,
    DateTimeOffset MeasuredAt,
    string? SafeDiagnostic,
    IReadOnlyList<NormalizedRedirectHop> Redirects,
    IReadOnlyList<NormalizedFinding> Findings,
    SafeHttpPhaseTiming? Timing = null);

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
    public const string MonitorSource = "WebHealthSafeHttpV1";

    private sealed record PageLengthMeasurement(long Bytes, string Source, long? TransferredLength);

    public static NormalizedCheckResult Normalize(NormalizeHttpResult input)
    {
        Validate(input.Policy);
        var redirects = input.Transport.Redirects.Select((hop, index) => new NormalizedRedirectHop(
            index + 1, hop.FromUrl, hop.ToUrl, hop.StatusCode, hop.IsLoop)).ToArray();
        var length = MeasurePageLength(input.Transport);
        var findings = Evaluate(input, ToBoundedMilliseconds(input.Transport.Duration), length).ToArray();
        var category = SelectFailureCategory(input.Transport, findings);
        return new(
            SelectOutcome(input.Transport, findings),
            category,
            input.Transport.StatusCode,
            ToBoundedMilliseconds(input.Transport.Duration),
            length?.TransferredLength,
            input.Transport.Failure is not null ? null : input.Transport.ResponseBytesRead,
            length?.Source,
            MonitorSource,
            input.MeasuredAt,
            Diagnostic(category),
            redirects,
            findings,
            input.Transport.Timing);
    }

    /// <summary>
    /// The page size this result is judged on (BR-P04), together with the label that says how
    /// it was obtained. The transferred length is preferred where the response advertised one,
    /// because that is what a visitor actually downloads; the decoded byte count is the
    /// fallback. Both values are stored either way — the label only says which one the rule
    /// used, so a report never has to guess whether a stored number is compressed. A failed
    /// exchange produced no page at all, so it has no length rather than a length of zero.
    /// </summary>
    private static PageLengthMeasurement? MeasurePageLength(SafeHttpTransportResult transport)
    {
        if (transport.Failure is not null)
        {
            return null;
        }

        if (transport.BodyTruncated)
        {
            return new(transport.ResponseBytesRead, PageLengthSources.BoundedDecoded, transport.TransferredLength);
        }

        return transport.TransferredLength is { } transferred
            ? new(transferred, PageLengthSources.TransferredContentLength, transferred)
            : new(transport.ResponseBytesRead, PageLengthSources.MeasuredDecoded, null);
    }

    private static IEnumerable<NormalizedFinding> Evaluate(
        NormalizeHttpResult input,
        int totalDurationMs,
        PageLengthMeasurement? length)
    {
        if (input.Transport.Failure is { } transportFailure)
        {
            if (transportFailure != SafeHttpFailureKind.Cancelled)
            {
                yield return FailureFinding(MapTransportFailure(transportFailure));
            }
            yield break;
        }

        var slowResponse = EvaluateResponseTime(totalDurationMs, input.Policy);
        if (slowResponse is not null)
        {
            yield return slowResponse;
        }

        var pageSize = EvaluatePageSize(length, input.Policy);
        if (pageSize is not null)
        {
            yield return pageSize;
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

    /// <summary>
    /// BR-P02. The finding is raised for every completed exchange, including a failing one: a
    /// server error that also took four seconds is two separate facts, and they track as two
    /// separate issues (BR-I04).
    /// </summary>
    private static NormalizedFinding? EvaluateResponseTime(int totalDurationMs, HttpResultPolicy policy)
    {
        var thresholds = policy.EffectiveResponseTime;
        var severity = PerformanceEvaluation.SelectResponseTimeSeverity(totalDurationMs, thresholds);
        if (severity == PerformanceSeverity.None)
        {
            return null;
        }

        var breached = severity == PerformanceSeverity.Critical
            ? thresholds.CriticalMs
            : thresholds.WarningMs;
        return Finding(
            HttpFailureCategories.SlowResponse,
            PerformanceRules.SlowResponse,
            $"{totalDurationMs} ms",
            $"Under {breached} ms",
            severity == PerformanceSeverity.Critical
                ? FindingSeverities.Critical
                : FindingSeverities.Warning);
    }

    /// <summary>
    /// BR-P04. A truncated body already raises <c>ResponseTooLarge</c> and its byte count is a
    /// lower bound rather than a measurement, so it is not also reported as an oversized page.
    /// </summary>
    private static NormalizedFinding? EvaluatePageSize(
        PageLengthMeasurement? length,
        HttpResultPolicy policy)
    {
        if (length is null || length.Source == PageLengthSources.BoundedDecoded)
        {
            return null;
        }

        return PerformanceEvaluation.SelectPageSizeSeverity(length.Bytes, policy.PageSizeWarningBytes)
            == PerformanceSeverity.None
            ? null
            : Finding(
                HttpFailureCategories.PageTooLarge,
                PerformanceRules.PageTooLarge,
                $"{length.Bytes} bytes ({length.Source})",
                $"Under {policy.PageSizeWarningBytes} bytes",
                FindingSeverities.Warning);
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
            (FindingSeverities.Critical, HttpFailureCategories.SlowResponse) => 410,
            (FindingSeverities.Critical, _) => 400,
            (FindingSeverities.High, _) => 350,
            (FindingSeverities.Warning, HttpFailureCategories.HttpsRequired) => 300,
            (FindingSeverities.Warning, HttpFailureCategories.SlowResponse) => 250,
            (FindingSeverities.Warning, HttpFailureCategories.PageTooLarge) => 240,
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

        return findings.Any(finding =>
            FindingSeverities.ToOutcome(finding.Severity) == HttpResultOutcomes.Critical)
                ? HttpResultOutcomes.Critical
                : findings.Count > 0 ? HttpResultOutcomes.Warning : HttpResultOutcomes.Healthy;
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
        Finding(category, $"Http.{category}", observed, expected, severity);

    private static NormalizedFinding Finding(
        string category,
        string ruleKey,
        string? observed,
        string? expected,
        string severity) =>
        new(category, ruleKey, severity, observed, expected, HttpIssueIdentity.Create(ruleKey));

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
        HttpFailureCategories.SlowResponse => "The response exceeded its response-time threshold.",
        HttpFailureCategories.PageTooLarge => "The page exceeded its size threshold.",
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
            || policy.PageSizeWarningBytes <= 0
            || policy.EffectiveResponseTime.WarningMs <= 0
            || policy.EffectiveResponseTime.CriticalMs < policy.EffectiveResponseTime.WarningMs
            || policy.ProductionHttpSeverity is not (FindingSeverities.Warning or FindingSeverities.Critical))
        {
            throw new ArgumentException("The HTTP result policy is invalid.", nameof(policy));
        }
    }
}
