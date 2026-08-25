namespace WebHealth.Infrastructure.PageAudits;

public sealed record PageAuditSchedulingOptions
{
    public const string SectionName = "PageAudits:Scheduling";

    public bool Enabled { get; init; }

    public int WorkerCount { get; init; } = 2;

    public int DispatchBatchSize { get; init; } = 10;

    public int ReconciliationBatchSize { get; init; } = 25;

    public TimeSpan ReconciliationDelay { get; init; } = TimeSpan.FromMinutes(5);


    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);

    public int MaximumAttempts { get; init; } = 3;
}

internal static class PageAuditQueueNames
{
    public const string PageAudits = "page-audits";
}
