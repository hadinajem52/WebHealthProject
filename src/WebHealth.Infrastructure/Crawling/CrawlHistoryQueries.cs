using WebHealth.Domain.Crawling;

namespace WebHealth.Infrastructure.Crawling;

internal static class CrawlHistoryQueries
{
    public static IQueryable<CrawlRun> Comparable(IQueryable<CrawlRun> runs) => runs.Where(run =>
        run.Status == CrawlRunStatuses.Completed && run.StopReason == CrawlStopReasons.FrontierExhausted
        && run.PagesFetched > 0 && !run.CoverageLimited);
}
