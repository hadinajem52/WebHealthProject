using WebHealth.Domain.Incidents;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class RetentionHistoryQueries(ApplicationDbContext database, DateTimeOffset asOf)
{
    public IQueryable<Guid> EligibleCompletedCheckIds(DateTimeOffset cutoff)
    {
        var held = new RetentionHoldQueries(database, asOf).LogicalCheckIds();
        var activeIncidentStatuses = IncidentStatuses.Active.ToArray();
        return database.LogicalChecks.Where(check => check.State == "Completed" && check.CompletedAt < cutoff
            && !held.Contains(check.Id)
            && !database.ExecutionLeases.Any(lease => lease.LogicalCheckId == check.Id)
            && !database.EndpointHealth.Any(health => health.EvidenceLogicalCheckId == check.Id)
            && !database.IncidentEvidence.Any(evidence => evidence.LogicalCheckId == check.Id
                && activeIncidentStatuses.Contains(evidence.Incident.Status)))
            .Select(check => check.Id);
    }
}
