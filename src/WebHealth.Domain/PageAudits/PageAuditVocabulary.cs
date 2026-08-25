namespace WebHealth.Domain.PageAudits;

public static class PageAuditProviders
{
    public const string PageSpeedInsights = "PageSpeedInsights";

    public static bool IsSupported(string value) => value is PageSpeedInsights;
}

public static class PageAuditCategories
{
    public const string Performance = "Performance";
    public const string Accessibility = "Accessibility";
    public const string BestPractices = "BestPractices";
    public const string Seo = "Seo";

    public const string PerformanceParameter = "performance";
    public const string AccessibilityParameter = "accessibility";
    public const string BestPracticesParameter = "best-practices";
    public const string SeoParameter = "seo";

    public static readonly string[] All = [Performance, Accessibility, BestPractices, Seo];

    public static bool IsSupported(string value) =>
        value is Performance or Accessibility or BestPractices or Seo;

    public static string Normalize(string? value) => value?.ToLowerInvariant() switch
    {
        "accessibility" => Accessibility,
        "bestpractices" or "best-practices" => BestPractices,
        "seo" => Seo,
        _ => Performance
    };

    public static string ToParameter(string category) => category switch
    {
        Performance => PerformanceParameter,
        Accessibility => AccessibilityParameter,
        BestPractices => BestPracticesParameter,
        Seo => SeoParameter,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unsupported category.")
    };
}

public static class PageAuditMonitorIdentity
{
    public const string MonitorType = PageAuditProviders.PageSpeedInsights;
}

public static class PageAuditPerformanceMetrics
{
    public const string FirstContentfulPaint = "first-contentful-paint";
    public const string LargestContentfulPaint = "largest-contentful-paint";
    public const string TotalBlockingTime = "total-blocking-time";
    public const string CumulativeLayoutShift = "cumulative-layout-shift";
    public const string SpeedIndex = "speed-index";

    public static readonly string[] All =
    [
        FirstContentfulPaint,
        LargestContentfulPaint,
        TotalBlockingTime,
        CumulativeLayoutShift,
        SpeedIndex
    ];
}

public static class PageAuditIncidentIssueKeys
{
    public static IReadOnlyList<string> All { get; } = PageAuditStrategies.All
        .SelectMany(strategy => PageAuditCategories.All.Select(category => CategoryScore(category, strategy))
            .Concat(PageAuditPerformanceMetrics.All.Select(metric => PerformanceMetric(metric, strategy))))
        .ToArray();

    public static string CategoryScore(string category, string strategy) =>
        $"v1|{PageAuditMonitorIdentity.MonitorType}|PageAudit.{category}.Score|{strategy}";

    public static string PerformanceMetric(string metric, string strategy) =>
        $"v1|{PageAuditMonitorIdentity.MonitorType}|PageAudit.Performance.{MetricName(metric)}|{strategy}";

    private static string MetricName(string metric) => metric switch
    {
        PageAuditPerformanceMetrics.FirstContentfulPaint => "FirstContentfulPaint",
        PageAuditPerformanceMetrics.LargestContentfulPaint => "LargestContentfulPaint",
        PageAuditPerformanceMetrics.TotalBlockingTime => "TotalBlockingTime",
        PageAuditPerformanceMetrics.CumulativeLayoutShift => "CumulativeLayoutShift",
        PageAuditPerformanceMetrics.SpeedIndex => "SpeedIndex",
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unsupported performance metric.")
    };
}

public static class PageAuditStrategies
{
    public const string Mobile = "Mobile";
    public const string Desktop = "Desktop";

    public const string MobileParameter = "mobile";
    public const string DesktopParameter = "desktop";

    public static readonly string[] All = [Mobile, Desktop];

    public static bool IsSupported(string value) => value is Mobile or Desktop;

    public static string Normalize(string? value) =>
        string.Equals(value, Desktop, StringComparison.OrdinalIgnoreCase) ? Desktop : Mobile;

    public static string ToParameter(string strategy) => strategy switch
    {
        Mobile => MobileParameter,
        Desktop => DesktopParameter,
        _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unsupported strategy.")
    };
}

public static class PageAuditSources
{
    public const string Scheduled = "Scheduled";
    public const string Manual = "Manual";

    public static bool IsSupported(string value) => value is Scheduled or Manual;
}

public static class PageAuditRunStatuses
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string CompletedWithWarnings = "CompletedWithWarnings";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";

    public static bool IsSupported(string value) =>
        value is Queued or Running or Completed or CompletedWithWarnings or Failed or Cancelled;

    public static bool IsTerminal(string value) =>
        value is Completed or CompletedWithWarnings or Failed or Cancelled;

    public static bool IsActive(string value) => value is Queued or Running;

    public static bool IsScored(string value) => value is Completed or CompletedWithWarnings;
}

public static class PageAuditItemStatuses
{
    public const string Passed = "Passed";
    public const string Failed = "Failed";

    public const string Scored = "Scored";

    public const string Manual = "Manual";
    public const string NotApplicable = "NotApplicable";
    public const string Informative = "Informative";

    public const string Error = "Error";

    public static bool IsSupported(string value) =>
        value is Passed or Failed or Scored or Manual or NotApplicable or Informative or Error;
}

public static class PageAuditComparability
{
    public const string Comparable = "Comparable";
    public const string LighthouseVersionChanged = "LighthouseVersionChanged";

    public static bool IsSupported(string value) => value is Comparable or LighthouseVersionChanged;
}

public static class PageAuditFailureCategories
{
    public const string ProviderRateLimited = "ProviderRateLimited";
    public const string ProviderUnavailable = "ProviderUnavailable";
    public const string ProviderTimeout = "ProviderTimeout";
    public const string ProviderAuthenticationFailed = "ProviderAuthenticationFailed";
    public const string TargetRejected = "TargetRejected";
    public const string CaptchaBlocked = "CaptchaBlocked";
    public const string LighthouseRuntimeError = "LighthouseRuntimeError";
    public const string ProviderContractInvalid = "ProviderContractInvalid";
    public const string ProviderResponseTooLarge = "ProviderResponseTooLarge";
    public const string ProviderResponseInvalid = "ProviderResponseInvalid";
    public const string Cancelled = "Cancelled";
    public const string UnknownProviderFailure = "UnknownProviderFailure";

    public static bool IsSupported(string value) =>
        value is ProviderRateLimited or ProviderUnavailable or ProviderTimeout
            or ProviderAuthenticationFailed or TargetRejected or CaptchaBlocked
            or LighthouseRuntimeError or ProviderContractInvalid or ProviderResponseTooLarge
            or ProviderResponseInvalid or Cancelled or UnknownProviderFailure;

    public static bool IsTransient(string value) =>
        value is ProviderRateLimited or ProviderUnavailable or ProviderTimeout
            or UnknownProviderFailure;
}

public static class PageAuditScoreDisplayModes
{
    public const string Binary = "binary";
    public const string Numeric = "numeric";
    public const string Manual = "manual";
    public const string NotApplicable = "notApplicable";
    public const string Informative = "informative";
    public const string Error = "error";
}

public static class PageAuditCadence
{
    public const int DefaultIntervalHours = 24;
    public const int MinimumIntervalHours = 6;
    public const int MaximumIntervalHours = 30 * 24;

    public static bool IsSupported(int intervalHours) =>
        intervalHours is >= MinimumIntervalHours and <= MaximumIntervalHours;
}
