using WebHealth.Domain.PageAudits;

namespace WebHealth.Infrastructure.PageAudits;

internal static class PageAuditHistoryQueries
{
    public static IQueryable<PageAuditRun> Scored(IQueryable<PageAuditRun> runs) => runs.Where(run =>
        run.RawScore != null && run.FinishedAt != null
        && (run.Status == PageAuditRunStatuses.Completed || run.Status == PageAuditRunStatuses.CompletedWithWarnings));
}
