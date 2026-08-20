using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.PageAudits;

/// <summary>
/// One configured third-party audit for one endpoint. Separate from <c>endpoint_monitor</c> on
/// purpose: this schedules a call to Google, not a check this application performs, and the two
/// have different cadences, different failure modes and different consent.
/// </summary>
public sealed class PageAuditTarget
{
    public Guid Id { get; set; }
    public Guid EndpointId { get; set; }

    /// <summary>
    /// Provider, category and strategy identify the audit rather than describe it. Together with
    /// the endpoint they are the uniqueness key, so enabling mobile does not overwrite desktop.
    /// </summary>
    public required string Provider { get; set; }
    public required string Category { get; set; }
    public required string Strategy { get; set; }

    /// <summary>The feature switch. False stops manual runs as well as scheduled ones.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Scheduled runs only. Kept apart from <see cref="IsEnabled" /> so an operator can stop the
    /// daily quota spend without losing the configuration or the history behind it.
    /// </summary>
    public bool SchedulingEnabled { get; set; }

    public int IntervalSeconds { get; set; }

    /// <summary>
    /// Where the cadence counts from, so a run that lands late does not walk every later run late
    /// with it. The same reason <c>endpoint_monitor</c> carries one.
    /// </summary>
    public DateTimeOffset ScheduleAnchor { get; set; }

    public DateTimeOffset NextDueAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }

    public Endpoint Endpoint { get; set; } = null!;
    public ICollection<PageAuditRun> Runs { get; } = [];
}

/// <summary>
/// One request to the provider and how it ended.
/// </summary>
/// <remarks>
/// <para>
/// The provider, category, strategy, locale and Lighthouse version are snapshots. Without them a
/// year-old score cannot be read: "82" means nothing unless what produced it is recorded beside
/// it, and a target edited afterwards would silently rewrite the meaning of stored history.
/// </para>
/// <para>
/// There is no raw-response column, by design. Google's payload is large, its audit <c>details</c>
/// are free-form and version-dependent, and keeping it would turn a score history into a store of
/// third-party page content.
/// </para>
/// </remarks>
public sealed class PageAuditRun
{
    public Guid Id { get; set; }
    public Guid PageAuditTargetId { get; set; }

    /// <summary>
    /// Denormalized from the target. It is the column the endpoint purge and every endpoint-scoped
    /// read filter on, and reaching it through a join is the shape Phase 5 lost time to.
    /// </summary>
    public Guid EndpointId { get; set; }

    public required string Source { get; set; }

    /// <summary>Null for a scheduled run, which nobody asked for personally.</summary>
    public Guid? InitiatedByUserId { get; set; }

    public required string Status { get; set; }

    /// <summary>The URL as sent. Snapshotted so the job cannot be handed a different one.</summary>
    public required string RequestedUrl { get; set; }

    /// <summary>Where the provider actually landed. Different from the request means a redirect.</summary>
    public string? FinalUrl { get; set; }

    /// <summary>
    /// The category score exactly as Lighthouse returned it, 0-1. The 0-100 number a reader sees
    /// is derived, so the two can never drift apart in storage.
    /// </summary>
    public decimal? RawScore { get; set; }

    public required string Provider { get; set; }
    public required string Category { get; set; }
    public required string Strategy { get; set; }
    public required string Locale { get; set; }

    /// <summary>Null until the provider answers; a run that never ran has no tool version.</summary>
    public string? LighthouseVersion { get; set; }

    /// <summary>
    /// The provider's run warnings as one bounded line. A count alone would say something happened
    /// without saying what, and the raw list is unbounded provider text.
    /// </summary>
    public string? WarningSummary { get; set; }

    public int AttemptCount { get; set; }

    /// <summary>Set only on a failed run, from the bounded vocabulary.</summary>
    public string? FailureCategory { get; set; }

    /// <summary>
    /// A bounded explanation safe to store and show. Google's own error body never lands here: the
    /// request URI it can carry carries the API key.
    /// </summary>
    public string? SafeDiagnostic { get; set; }

    public DateTimeOffset QueuedAt { get; set; }

    /// <summary>When the provider says it ran the audit, which is not when we asked.</summary>
    public DateTimeOffset? AnalysisAt { get; set; }

    /// <summary>Null while the run is live, so an interrupted worker leaves a visibly open run.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>
    /// The worker's claim. Token and expiry move together: a claim with no expiry could never be
    /// reclaimed after a crash, and an expiry with no token identifies nobody.
    /// </summary>
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public PageAuditTarget Target { get; set; } = null!;
    public ICollection<PageAuditItem> Items { get; } = [];
}

/// <summary>
/// One normalized Lighthouse audit inside one run. Only audits the SEO category actually
/// references are stored: the response carries audits belonging to other categories too, and
/// keeping those would attribute them to a score they took no part in.
/// </summary>
public sealed class PageAuditItem
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }

    /// <summary>
    /// Lighthouse's own stable identifier, which is what makes an audit the same audit across
    /// runs. Comparison keys on this rather than on the title, which is translated prose.
    /// </summary>
    public required string AuditId { get; set; }

    public required string Status { get; set; }

    /// <summary>Null for manual, not-applicable, informative and errored audits.</summary>
    public decimal? Score { get; set; }

    /// <summary>The provider's mode, kept so a status can be traced back to what produced it.</summary>
    public string? ScoreDisplayMode { get; set; }

    /// <summary>The audit's contribution to the category score, for explanation only.</summary>
    public double Weight { get; set; }

    public string? GroupName { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? DisplayValue { get; set; }
    public string? Explanation { get; set; }
    public string? ErrorMessage { get; set; }

    public PageAuditRun Run { get; set; } = null!;
}
