using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Application.PageAudits;

namespace WebHealth.Infrastructure.PageAudits;

/// <summary>
/// The page-audit queue's only job, and the sole occupant of its own Hangfire server.
/// </summary>
/// <remarks>
/// <para>
/// It takes a run id and nothing else. Everything the audit needs — the URL, the strategy, the
/// locale — was snapshotted onto the run when it was opened, so no caller can hand this job a
/// different URL than the one the configuration approved.
/// </para>
/// <para>
/// <c>AutomaticRetry(Attempts = 0)</c> because the application counts attempts itself, in
/// <c>attempt_count</c>. Two retry mechanisms would disagree about how many times we have already
/// asked Google to load somebody's page, and Hangfire's count is not the one stored beside the run.
/// </para>
/// </remarks>
public sealed class PageAuditRunJob(
    PageAuditExecutionService executionService,
    IPageAuditQueue queue)
{
    [Queue(PageAuditQueueNames.PageAudits)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        var outcome = await executionService.ExecuteAsync(runId, cancellationToken);
        if (outcome.ShouldRetry)
        {
            queue.Schedule(runId, outcome.RetryAfter!.Value);
        }
    }
}

/// <summary>The recurring dispatcher and the reconciliation sweep, on the same isolated queue.</summary>
public sealed class PageAuditDispatchJob(PageAuditSchedulingService schedulingService)
{
    [Queue(PageAuditQueueNames.PageAudits)]
    [AutomaticRetry(Attempts = 0)]
    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await schedulingService.DispatchDueAsync(cancellationToken);
        await schedulingService.ReconcileAsync(cancellationToken);
    }
}

internal sealed class HangfirePageAuditQueue(IBackgroundJobClient backgroundJobs) : IPageAuditQueue
{
    public void Enqueue(Guid runId) =>
        backgroundJobs.Enqueue<PageAuditRunJob>(job => job.ExecuteAsync(runId, CancellationToken.None));

    public void Schedule(Guid runId, TimeSpan delay) =>
        backgroundJobs.Schedule<PageAuditRunJob>(
            job => job.ExecuteAsync(runId, CancellationToken.None), delay);
}

/// <summary>
/// Used when the feature is switched off. It throws rather than doing nothing: a run row committed
/// with no way to reach a worker would sit Queued forever, looking like work in progress.
/// </summary>
internal sealed class DisabledPageAuditQueue : IPageAuditQueue
{
    public void Enqueue(Guid runId) => throw Unavailable();

    public void Schedule(Guid runId, TimeSpan delay) => throw Unavailable();

    private static InvalidOperationException Unavailable() => new(
        "Page audit scheduling is disabled; no queue is available to run page audits.");
}

public static class PageAuditSchedulingApplicationBuilderExtensions
{
    public static WebApplication UsePageAuditScheduling(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.Services.GetRequiredService<PageAuditSchedulingOptions>();
        if (!options.Enabled)
        {
            return app;
        }

        // Every fifteen minutes rather than hourly: the dispatcher also runs the reconciliation
        // sweep, and a run whose job was lost should not wait an hour to be noticed.
        app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<PageAuditDispatchJob>(
            "page-audit-dispatch",
            PageAuditQueueNames.PageAudits,
            job => job.DispatchAsync(CancellationToken.None),
            "*/15 * * * *",
            new RecurringJobOptions());
        return app;
    }
}
