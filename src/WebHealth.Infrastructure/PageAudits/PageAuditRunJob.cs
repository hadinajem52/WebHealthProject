using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Application.PageAudits;

namespace WebHealth.Infrastructure.PageAudits;

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

        app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<PageAuditDispatchJob>(
            "page-audit-dispatch",
            PageAuditQueueNames.PageAudits,
            job => job.DispatchAsync(CancellationToken.None),
            "*/15 * * * *",
            new RecurringJobOptions());
        return app;
    }
}
