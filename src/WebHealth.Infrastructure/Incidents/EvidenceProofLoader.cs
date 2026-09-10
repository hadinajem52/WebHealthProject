using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Incidents;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Incidents;

internal sealed class EvidenceProofLoader(ApplicationDbContext dbContext)
{
    public async Task<IReadOnlyDictionary<Guid, IncidentEvidenceProof>> LoadAsync(
        IEnumerable<Guid?> logicalCheckIds,
        string issueKey,
        CancellationToken cancellationToken = default)
    {
        var ids = logicalCheckIds.Where(id => id is not null).Select(id => id!.Value).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, IncidentEvidenceProof>();
        }

        var results = await dbContext.CheckResults.AsNoTracking()
            .Where(result => ids.Contains(result.LogicalCheckId))
            .Select(result => new
            {
                result.LogicalCheckId,
                result.SafeDiagnostic,
                FailureConfirmationCount = (int?)result.LogicalCheck.ConfigurationSnapshot.FailureConfirmationCount,
                Finding = dbContext.Findings
                    .Where(finding => finding.LogicalCheckId == result.LogicalCheckId
                        && finding.IssueKey == issueKey)
                    .OrderBy(finding => finding.RuleKey)
                    .Select(finding => new
                    {
                        finding.Severity,
                        finding.ObservedValue,
                        finding.ExpectedValue
                    })
                    .FirstOrDefault()
            })
            .ToArrayAsync(cancellationToken);

        return results.ToDictionary(
            result => result.LogicalCheckId,
            result => new IncidentEvidenceProof(
                result.Finding?.Severity,
                result.Finding?.ObservedValue,
                result.Finding?.ExpectedValue,
                result.SafeDiagnostic,
                result.FailureConfirmationCount));
    }
}
