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
                result.FailureCategory,
                result.HttpStatus,
                result.TotalDurationMs,
                result.SafeDiagnostic,
                FailureConfirmationCount = (int?)result.LogicalCheck.ConfigurationSnapshot.FailureConfirmationCount,
                ObservedValue = dbContext.Findings
                    .Where(finding => finding.LogicalCheckId == result.LogicalCheckId
                        && finding.IssueKey == issueKey)
                    .OrderBy(finding => finding.RuleKey)
                    .Select(finding => finding.ObservedValue)
                    .FirstOrDefault(),
                ExpectedValue = dbContext.Findings
                    .Where(finding => finding.LogicalCheckId == result.LogicalCheckId
                        && finding.IssueKey == issueKey)
                    .OrderBy(finding => finding.RuleKey)
                    .Select(finding => finding.ExpectedValue)
                    .FirstOrDefault()
            })
            .ToArrayAsync(cancellationToken);

        return results.ToDictionary(
            result => result.LogicalCheckId,
            result => new IncidentEvidenceProof(
                result.FailureCategory,
                result.HttpStatus,
                result.TotalDurationMs,
                result.SafeDiagnostic,
                result.ObservedValue,
                result.ExpectedValue,
                result.FailureConfirmationCount));
    }
}
