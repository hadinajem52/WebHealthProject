namespace WebHealth.Domain.PageAudits;

/// <summary>
/// Who ran the audit. Stored on every run so history stays interpretable after the target's
/// configuration changes, and so a later provider can be introduced without rewriting old rows.
/// </summary>
public static class PageAuditProviders
{
    public const string PageSpeedInsights = "PageSpeedInsights";

    public static bool IsSupported(string value) => value is PageSpeedInsights;
}

/// <summary>The Lighthouse category the run asked for. V1 asks for one and stores which one.</summary>
public static class PageAuditCategories
{
    public const string Seo = "Seo";

    /// <summary>The provider's own spelling, which is what goes on the wire.</summary>
    public const string SeoParameter = "seo";

    public static bool IsSupported(string value) => value is Seo;
}

public static class PageAuditStrategies
{
    public const string Mobile = "Mobile";
    public const string Desktop = "Desktop";

    public const string MobileParameter = "mobile";
    public const string DesktopParameter = "desktop";

    public static bool IsSupported(string value) => value is Mobile or Desktop;

    /// <summary>
    /// The query value for a stored strategy. Always sent explicitly: the API's own default is
    /// desktop, so omitting it would silently audit a different form factor than the one recorded.
    /// </summary>
    public static string ToParameter(string strategy) => strategy switch
    {
        Mobile => MobileParameter,
        Desktop => DesktopParameter,
        _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unsupported strategy.")
    };
}

/// <summary>Whether a run was asked for by the scheduler or by a person.</summary>
public static class PageAuditSources
{
    public const string Scheduled = "Scheduled";
    public const string Manual = "Manual";

    public static bool IsSupported(string value) => value is Scheduled or Manual;
}

/// <summary>
/// A run's lifecycle. <c>Completed</c> and <c>CompletedWithWarnings</c> are both successes: a run
/// that produced a trustworthy score is complete even when every audit inside it failed.
/// </summary>
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

    /// <summary>A run that will never change again. Only these carry a finish time.</summary>
    public static bool IsTerminal(string value) =>
        value is Completed or CompletedWithWarnings or Failed or Cancelled;

    /// <summary>A run that has been asked for and not yet resolved. At most one per target.</summary>
    public static bool IsActive(string value) => value is Queued or Running;

    /// <summary>The two statuses that carry a score worth reading.</summary>
    public static bool IsScored(string value) => value is Completed or CompletedWithWarnings;
}

/// <summary>
/// What one Lighthouse audit says, reduced to the distinctions this feature acts on. Manual and
/// not-applicable are separate statuses rather than absences: "a person still has to check this"
/// and "this page passed" must never be shown as the same thing, and neither is a failure.
/// </summary>
public static class PageAuditItemStatuses
{
    public const string Passed = "Passed";
    public const string Failed = "Failed";

    /// <summary>
    /// A numeric audit. Deliberately not split into pass/fail: Lighthouse publishes no threshold
    /// for these, so inventing one would attribute a judgement to the provider it never made.
    /// </summary>
    public const string Scored = "Scored";

    public const string Manual = "Manual";
    public const string NotApplicable = "NotApplicable";
    public const string Informative = "Informative";

    /// <summary>The audit itself could not run. Not a failure of the page.</summary>
    public const string Error = "Error";

    public static bool IsSupported(string value) =>
        value is Passed or Failed or Scored or Manual or NotApplicable or Informative or Error;
}

/// <summary>
/// Whether two runs may be read as a like-for-like change. A Lighthouse major version can add,
/// remove or redefine audits, so a delta across one is a different kind of number and is labelled
/// rather than silently presented as a regression.
/// </summary>
public static class PageAuditComparability
{
    public const string Comparable = "Comparable";
    public const string LighthouseVersionChanged = "LighthouseVersionChanged";

    public static bool IsSupported(string value) => value is Comparable or LighthouseVersionChanged;
}

/// <summary>
/// Why a run did not produce a score. Bounded on purpose: the provider's own error text is never
/// stored verbatim, because it can carry the request URI and the request URI carries the API key.
/// </summary>
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

    /// <summary>
    /// Failures worth another attempt inside the bounded retry budget. A rejected target or a
    /// misconfigured key will fail identically every time, so retrying them only spends quota.
    /// </summary>
    public static bool IsTransient(string value) =>
        value is ProviderRateLimited or ProviderUnavailable or ProviderTimeout
            or UnknownProviderFailure;
}

/// <summary>
/// The provider's own <c>scoreDisplayMode</c> values. Named here so the mapping in
/// <see cref="PageAuditNormalization" /> reads against constants rather than loose strings.
/// </summary>
public static class PageAuditScoreDisplayModes
{
    public const string Binary = "binary";
    public const string Numeric = "numeric";
    public const string Manual = "manual";
    public const string NotApplicable = "notApplicable";
    public const string Informative = "informative";
    public const string Error = "error";
}

/// <summary>
/// How often an audit may run. The floor is a courtesy limit rather than a performance one: each
/// run asks Google to load somebody's page, and a tighter cadence spends quota faster than a
/// technical SEO score can meaningfully change.
/// </summary>
public static class PageAuditCadence
{
    public const int DefaultIntervalHours = 24;
    public const int MinimumIntervalHours = 6;
    public const int MaximumIntervalHours = 30 * 24;

    public static bool IsSupported(int intervalHours) =>
        intervalHours is >= MinimumIntervalHours and <= MaximumIntervalHours;
}
