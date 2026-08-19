using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace WebHealth.Infrastructure.Seo;

public sealed class SeoSchedulingOptions
{
    public const string SectionName = "Seo:Scheduling";

    public bool Enabled { get; init; }

    /// <summary>
    /// How long one origin's robots.txt is trusted. Long enough that fifty endpoints on a host
    /// cost one fetch a day, short enough that a site newly blocked from search is noticed the
    /// same day it happens.
    /// </summary>
    public int RobotsTtlHours { get; init; } = 24;

    public int RefreshBatchSize { get; init; } = 25;

    public int FetchTimeoutSeconds { get; init; } = 15;
}

internal static class SeoQueueNames
{
    public const string Seo = "seo";
}

internal sealed class RobotsRefreshJob(RobotsRefreshService refreshService)
{
    [Queue(SeoQueueNames.Seo)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RefreshAsync(CancellationToken cancellationToken) =>
        await refreshService.RefreshDueAsync(cancellationToken);
}

public static class SeoSchedulingApplicationBuilderExtensions
{
    public static WebApplication UseSeoScheduling(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<SeoSchedulingOptions>();
        if (!options.Enabled)
        {
            return app;
        }

        app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<RobotsRefreshJob>(
            "seo-robots-refresh",
            SeoQueueNames.Seo,
            job => job.RefreshAsync(CancellationToken.None),
            Cron.Hourly(),
            new RecurringJobOptions());
        return app;
    }
}
