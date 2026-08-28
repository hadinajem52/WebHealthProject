using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.PageAudits;

public sealed class PageAuditTarget
{
    public Guid Id { get; set; }
    public Guid EndpointId { get; set; }

    public required string Provider { get; set; }
    public required string Category { get; set; }
    public required string Strategy { get; set; }

    public bool IsEnabled { get; set; }

    public bool SchedulingEnabled { get; set; }

    public int IntervalSeconds { get; set; }

    public DateTimeOffset ScheduleAnchor { get; set; }

    public DateTimeOffset NextDueAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }

    public Endpoint Endpoint { get; set; } = null!;
    public ICollection<PageAuditRun> Runs { get; } = [];
}

public sealed class PageAuditRun
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid PageAuditTargetId { get; set; }

    public Guid EndpointId { get; set; }

    public required string Source { get; set; }

    public Guid? InitiatedByUserId { get; set; }

    public required string Status { get; set; }

    public required string RequestedUrl { get; set; }

    public string? FinalUrl { get; set; }

    public decimal? RawScore { get; set; }

    public required string Provider { get; set; }
    public required string Category { get; set; }
    public required string Strategy { get; set; }
    public required string Locale { get; set; }

    public string? LighthouseVersion { get; set; }

    public string? WarningSummary { get; set; }

    public int AttemptCount { get; set; }

    public string? FailureCategory { get; set; }

    public string? SafeDiagnostic { get; set; }

    public DateTimeOffset QueuedAt { get; set; }

    public DateTimeOffset? AnalysisAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public PageAuditTarget Target { get; set; } = null!;
    public ICollection<PageAuditItem> Items { get; } = [];
}

public sealed class PageAuditItem
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }

    public required string AuditId { get; set; }

    public required string Status { get; set; }

    public decimal? Score { get; set; }

    public string? ScoreDisplayMode { get; set; }

    public decimal? NumericValue { get; set; }
    public string? NumericUnit { get; set; }

    public double Weight { get; set; }

    public string? GroupName { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? DisplayValue { get; set; }
    public string? Explanation { get; set; }
    public string? ErrorMessage { get; set; }

    public PageAuditRun Run { get; set; } = null!;
}

public sealed class PageAuditIncidentPolicyEntity
{
    public Guid EndpointId { get; set; }
    public bool IncidentsEnabled { get; set; }
    public bool PerformanceScoreEnabled { get; set; }
    public int PerformanceMinimumScore { get; set; }
    public bool AccessibilityScoreEnabled { get; set; }
    public int AccessibilityMinimumScore { get; set; }
    public bool BestPracticesScoreEnabled { get; set; }
    public int BestPracticesMinimumScore { get; set; }
    public bool SeoScoreEnabled { get; set; }
    public int SeoMinimumScore { get; set; }
    public bool FirstContentfulPaintEnabled { get; set; }
    public decimal FirstContentfulPaintMaximum { get; set; }
    public bool LargestContentfulPaintEnabled { get; set; }
    public decimal LargestContentfulPaintMaximum { get; set; }
    public bool TotalBlockingTimeEnabled { get; set; }
    public decimal TotalBlockingTimeMaximum { get; set; }
    public bool CumulativeLayoutShiftEnabled { get; set; }
    public decimal CumulativeLayoutShiftMaximum { get; set; }
    public bool SpeedIndexEnabled { get; set; }
    public decimal SpeedIndexMaximum { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public long Version { get; set; }
    public Endpoint Endpoint { get; set; } = null!;
}
