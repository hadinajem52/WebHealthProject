using WebHealth.Application.Health;
using WebHealth.Application.Monitoring;
using WebHealth.Domain.PageAudits;

namespace WebHealth.Application.PageAudits;

public sealed record PageAuditIncidentPolicy(
    Guid EndpointId,
    bool IncidentsEnabled,
    bool PerformanceScoreEnabled,
    int PerformanceMinimumScore,
    bool AccessibilityScoreEnabled,
    int AccessibilityMinimumScore,
    bool BestPracticesScoreEnabled,
    int BestPracticesMinimumScore,
    bool SeoScoreEnabled,
    int SeoMinimumScore,
    bool FirstContentfulPaintEnabled,
    decimal FirstContentfulPaintMaximum,
    bool LargestContentfulPaintEnabled,
    decimal LargestContentfulPaintMaximum,
    bool TotalBlockingTimeEnabled,
    decimal TotalBlockingTimeMaximum,
    bool CumulativeLayoutShiftEnabled,
    decimal CumulativeLayoutShiftMaximum,
    bool SpeedIndexEnabled,
    decimal SpeedIndexMaximum,
    long Version);

public sealed record UpdatePageAuditIncidentPolicy(
    bool IncidentsEnabled,
    bool PerformanceScoreEnabled,
    int PerformanceMinimumScore,
    bool AccessibilityScoreEnabled,
    int AccessibilityMinimumScore,
    bool BestPracticesScoreEnabled,
    int BestPracticesMinimumScore,
    bool SeoScoreEnabled,
    int SeoMinimumScore,
    bool FirstContentfulPaintEnabled,
    decimal FirstContentfulPaintMaximum,
    bool LargestContentfulPaintEnabled,
    decimal LargestContentfulPaintMaximum,
    bool TotalBlockingTimeEnabled,
    decimal TotalBlockingTimeMaximum,
    bool CumulativeLayoutShiftEnabled,
    decimal CumulativeLayoutShiftMaximum,
    bool SpeedIndexEnabled,
    decimal SpeedIndexMaximum,
    long Version);

public sealed record PageAuditIncidentPolicyUpdateResult(
    bool Succeeded,
    bool Conflict,
    PageAuditIncidentPolicy Policy,
    IReadOnlyList<string> Errors);

public interface IPageAuditIncidentPolicyService
{
    Task<PageAuditIncidentPolicy> GetAsync(
        Guid endpointId,
        CancellationToken cancellationToken = default);

    Task<PageAuditIncidentPolicyUpdateResult> UpdateAsync(
        Guid endpointId,
        UpdatePageAuditIncidentPolicy command,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}

public interface IPageAuditIncidentAutomationService
{
    Task ApplyAsync(
        PageAuditRunContext run,
        PageAuditProviderResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record PageAuditRunContext(
    Guid Id,
    Guid EndpointId,
    string Source,
    string Category,
    string Strategy);

public sealed record PageAuditMetricDefinition(
    string AuditId,
    string Label,
    string Unit,
    decimal SuggestedMaximum,
    decimal MaximumAllowed,
    int DecimalPlaces);

public static class PageAuditMetricDefinitions
{
    public static readonly IReadOnlyList<PageAuditMetricDefinition> All =
    [
        new(PageAuditPerformanceMetrics.FirstContentfulPaint, "First Contentful Paint", "ms", 1800, 600000, 0),
        new(PageAuditPerformanceMetrics.LargestContentfulPaint, "Largest Contentful Paint", "ms", 2500, 600000, 0),
        new(PageAuditPerformanceMetrics.TotalBlockingTime, "Total Blocking Time", "ms", 200, 600000, 0),
        new(PageAuditPerformanceMetrics.CumulativeLayoutShift, "Cumulative Layout Shift", "score", 0.1m, 10, 3),
        new(PageAuditPerformanceMetrics.SpeedIndex, "Speed Index", "ms", 3400, 600000, 0)
    ];

    public static PageAuditMetricDefinition Get(string auditId) =>
        All.Single(metric => metric.AuditId == auditId);
}

public sealed record PageAuditIncidentObservation(
    string IssueKey,
    string RuleKey,
    string Label,
    string Severity,
    decimal Actual,
    decimal Threshold,
    string Unit);

public sealed record PageAuditIncidentEvaluation(
    IReadOnlyList<PageAuditIncidentObservation> Observations,
    IReadOnlyList<string> EvaluatedIssueKeys,
    IReadOnlyList<string> IndeterminateIssueKeys)
{
    public IReadOnlyList<ObservedIssue> ObservedIssues =>
        Observations.Select(observation => new ObservedIssue(
            observation.IssueKey,
            observation.Severity,
            1)).ToArray();
}

public static class PageAuditIncidentEvaluator
{
    public static PageAuditIncidentEvaluation Evaluate(
        PageAuditIncidentPolicy policy,
        string category,
        string strategy,
        decimal categoryScore,
        IReadOnlyCollection<PageAuditProviderItem> items,
        IReadOnlyCollection<string> existingIssueKeys)
    {
        var observations = new List<PageAuditIncidentObservation>();
        var evaluated = new HashSet<string>(StringComparer.Ordinal);
        EvaluateCategory(policy, category, strategy, categoryScore, observations, evaluated);
        if (category == PageAuditCategories.Performance)
        {
            EvaluateMetrics(policy, strategy, items, observations, evaluated);
        }

        var indeterminate = existingIssueKeys
            .Where(issueKey => !evaluated.Contains(issueKey))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new(observations, [.. evaluated.Order(StringComparer.Ordinal)], indeterminate);
    }

    public static IReadOnlyList<string> Validate(UpdatePageAuditIncidentPolicy policy)
    {
        var errors = new List<string>();
        ValidateScore(policy.PerformanceMinimumScore, "Performance", errors);
        ValidateScore(policy.AccessibilityMinimumScore, "Accessibility", errors);
        ValidateScore(policy.BestPracticesMinimumScore, "Best Practices", errors);
        ValidateScore(policy.SeoMinimumScore, "SEO", errors);
        ValidateMetric(PageAuditPerformanceMetrics.FirstContentfulPaint, policy.FirstContentfulPaintMaximum, errors);
        ValidateMetric(PageAuditPerformanceMetrics.LargestContentfulPaint, policy.LargestContentfulPaintMaximum, errors);
        ValidateMetric(PageAuditPerformanceMetrics.TotalBlockingTime, policy.TotalBlockingTimeMaximum, errors);
        ValidateMetric(PageAuditPerformanceMetrics.CumulativeLayoutShift, policy.CumulativeLayoutShiftMaximum, errors);
        ValidateMetric(PageAuditPerformanceMetrics.SpeedIndex, policy.SpeedIndexMaximum, errors);
        return errors;
    }

    private static void EvaluateCategory(
        PageAuditIncidentPolicy policy,
        string category,
        string strategy,
        decimal rawScore,
        ICollection<PageAuditIncidentObservation> observations,
        ISet<string> evaluated)
    {
        var configured = CategoryThreshold(policy, category);
        if (configured is null)
        {
            return;
        }

        var issueKey = PageAuditIncidentIssueKeys.CategoryScore(category, strategy);
        evaluated.Add(issueKey);
        var score = PageAuditNormalization.ToDisplayScore(rawScore);
        if (score >= configured.Value)
        {
            return;
        }

        observations.Add(new(
            issueKey,
            $"PageAudit.{category}.Score",
            $"{category} score",
            score < 50 ? FindingSeverities.Critical : FindingSeverities.Warning,
            score,
            configured.Value,
            "score"));
    }

    private static void EvaluateMetrics(
        PageAuditIncidentPolicy policy,
        string strategy,
        IReadOnlyCollection<PageAuditProviderItem> items,
        ICollection<PageAuditIncidentObservation> observations,
        ISet<string> evaluated)
    {
        var byId = items.ToDictionary(item => item.AuditId, StringComparer.Ordinal);
        foreach (var definition in PageAuditMetricDefinitions.All)
        {
            var threshold = MetricThreshold(policy, definition.AuditId);
            if (threshold is null || !byId.TryGetValue(definition.AuditId, out var item)
                || item.NumericValue is null)
            {
                continue;
            }

            var issueKey = PageAuditIncidentIssueKeys.PerformanceMetric(definition.AuditId, strategy);
            evaluated.Add(issueKey);
            if (item.NumericValue <= threshold.Value)
            {
                continue;
            }

            observations.Add(new(
                issueKey,
                $"PageAudit.Performance.{definition.AuditId}",
                definition.Label,
                item.Score < 0.5m ? FindingSeverities.Critical : FindingSeverities.Warning,
                item.NumericValue.Value,
                threshold.Value,
                definition.Unit));
        }
    }

    private static int? CategoryThreshold(PageAuditIncidentPolicy policy, string category) => category switch
    {
        PageAuditCategories.Performance when policy.PerformanceScoreEnabled => policy.PerformanceMinimumScore,
        PageAuditCategories.Accessibility when policy.AccessibilityScoreEnabled => policy.AccessibilityMinimumScore,
        PageAuditCategories.BestPractices when policy.BestPracticesScoreEnabled => policy.BestPracticesMinimumScore,
        PageAuditCategories.Seo when policy.SeoScoreEnabled => policy.SeoMinimumScore,
        _ => null
    };

    private static decimal? MetricThreshold(PageAuditIncidentPolicy policy, string auditId) => auditId switch
    {
        PageAuditPerformanceMetrics.FirstContentfulPaint when policy.FirstContentfulPaintEnabled =>
            policy.FirstContentfulPaintMaximum,
        PageAuditPerformanceMetrics.LargestContentfulPaint when policy.LargestContentfulPaintEnabled =>
            policy.LargestContentfulPaintMaximum,
        PageAuditPerformanceMetrics.TotalBlockingTime when policy.TotalBlockingTimeEnabled =>
            policy.TotalBlockingTimeMaximum,
        PageAuditPerformanceMetrics.CumulativeLayoutShift when policy.CumulativeLayoutShiftEnabled =>
            policy.CumulativeLayoutShiftMaximum,
        PageAuditPerformanceMetrics.SpeedIndex when policy.SpeedIndexEnabled => policy.SpeedIndexMaximum,
        _ => null
    };

    private static void ValidateScore(int value, string label, ICollection<string> errors)
    {
        if (value is < 0 or > 100)
        {
            errors.Add($"{label} minimum score must be between 0 and 100.");
        }
    }

    private static void ValidateMetric(string auditId, decimal value, ICollection<string> errors)
    {
        var definition = PageAuditMetricDefinitions.Get(auditId);
        if (value < 0 || value > definition.MaximumAllowed)
        {
            errors.Add($"{definition.Label} maximum must be between 0 and {definition.MaximumAllowed}.");
        }
    }
}
