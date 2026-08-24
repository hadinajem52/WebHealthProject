using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Health;
using WebHealth.Application.Maintenance;
using WebHealth.Application.PageAudits;
using WebHealth.Domain.Health;
using WebHealth.Domain.Incidents;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.Health;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.PageAudits;

internal sealed class PageAuditIncidentAutomationService(
    ApplicationDbContext dbContext,
    IPageAuditIncidentPolicyService policyService,
    IMaintenanceEvaluator maintenanceEvaluator,
    IncidentAutomationService incidentAutomation,
    ILogger<PageAuditIncidentAutomationService> logger) : IPageAuditIncidentAutomationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task ApplyAsync(
        PageAuditRunContext claim,
        PageAuditProviderResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (claim.Source != PageAuditSources.Scheduled)
        {
            return;
        }

        var policy = await policyService.GetAsync(cancellationToken);
        if (!policy.IncidentsEnabled)
        {
            return;
        }

        if (result.CategoryScore is not { } categoryScore)
        {
            return;
        }

        var monitor = await dbContext.EndpointMonitors
            .Include(candidate => candidate.Endpoint)
                .ThenInclude(endpoint => endpoint.Environment)
                    .ThenInclude(environment => environment.Website)
            .SingleOrDefaultAsync(candidate => candidate.EndpointId == claim.EndpointId
                && candidate.MonitorType == RegistryDefaults.PageAuditMonitorType
                && candidate.DeletedAt == null,
                cancellationToken);
        if (monitor is null)
        {
            logger.LogWarning(
                "PageAudit incident evaluation skipped because its monitor is missing. PageAuditRunId={PageAuditRunId} EndpointId={EndpointId}",
                claim.Id,
                claim.EndpointId);
            return;
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM web_health.endpoint_monitor WHERE id = {monitor.Id} FOR UPDATE",
            cancellationToken);
        var states = await dbContext.IssueStates
            .FromSqlInterpolated($"SELECT * FROM web_health.issue_state WHERE endpoint_monitor_id = {monitor.Id} FOR UPDATE")
            .ToListAsync(cancellationToken);
        var activeSeverities = await dbContext.Incidents.AsNoTracking()
            .Where(incident => incident.EndpointMonitorId == monitor.Id
                && IncidentStatuses.Active.Contains(incident.Status))
            .Select(incident => incident.Severity)
            .ToArrayAsync(cancellationToken);
        var currentStatus = activeSeverities.Contains(IncidentSeverities.Critical, StringComparer.Ordinal)
            ? EndpointHealthStatuses.Critical
            : activeSeverities.Length > 0
                ? EndpointHealthStatuses.Warning
                : EndpointHealthStatuses.Unknown;
        var evaluation = PageAuditIncidentEvaluator.Evaluate(
            policy,
            claim.Category,
            claim.Strategy,
            categoryScore,
            result.Items,
            PageAuditIncidentIssueKeys.All);
        var decision = HealthConfirmationEngine.Evaluate(new(
            currentStatus,
            states.Select(ToCounter).ToArray(),
            evaluation.ObservedIssues,
            evaluation.IndeterminateIssueKeys,
            evaluation.Observations.Count == 0,
            RegistryDefaults.PageAuditRecoveryConfirmationCount,
            HealthCounterMode.Count));
        ApplyIssueCounters(monitor.Id, states, decision.Issues, now);

        var maintenance = await maintenanceEvaluator.FindActiveAsync(
            monitor.Id, result.AnalysisAt, cancellationToken);
        var ownerSubjectId = monitor.Endpoint.OwnerSubjectId
            ?? monitor.Endpoint.Environment.Website.OwnerSubjectId;
        var severities = evaluation.Observations.ToDictionary(
            observation => observation.IssueKey,
            observation => observation.Severity,
            StringComparer.Ordinal);
        var snapshot = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            category = claim.Category,
            strategy = claim.Strategy,
            categoryScore = PageAuditNormalization.ToDisplayScore(categoryScore),
            measuredAt = result.AnalysisAt,
            thresholds = evaluation.Observations.Select(observation => new
            {
                observation.RuleKey,
                observation.Actual,
                observation.Threshold,
                observation.Unit
            })
        }, SerializerOptions);
        await incidentAutomation.ApplyObservationAsync(
            new(
                monitor.Id,
                ownerSubjectId,
                result.AnalysisAt,
                severities,
                null,
                claim.Id,
                snapshot),
            decision,
            maintenance is not null,
            now,
            cancellationToken);
    }

    private void ApplyIssueCounters(
        Guid endpointMonitorId,
        IReadOnlyCollection<IssueState> currentStates,
        IReadOnlyCollection<HealthIssueCounter> decisions,
        DateTimeOffset now)
    {
        var current = currentStates.ToDictionary(state => state.IssueKey, StringComparer.Ordinal);
        foreach (var decision in decisions)
        {
            if (!current.TryGetValue(decision.IssueKey, out var state))
            {
                dbContext.IssueStates.Add(new IssueState
                {
                    Id = Guid.NewGuid(),
                    EndpointMonitorId = endpointMonitorId,
                    IssueKey = decision.IssueKey,
                    ConsecutiveFailures = decision.ConsecutiveFailures,
                    ConsecutiveRecoveries = decision.ConsecutiveRecoveries,
                    UpdatedAt = now,
                    Version = 1
                });
                continue;
            }

            if (state.ConsecutiveFailures == decision.ConsecutiveFailures
                && state.ConsecutiveRecoveries == decision.ConsecutiveRecoveries)
            {
                continue;
            }

            state.ConsecutiveFailures = decision.ConsecutiveFailures;
            state.ConsecutiveRecoveries = decision.ConsecutiveRecoveries;
            state.UpdatedAt = now;
            state.Version++;
        }
    }

    private static HealthIssueCounter ToCounter(IssueState state) => new(
        state.IssueKey,
        state.ConsecutiveFailures,
        state.ConsecutiveRecoveries);
}
