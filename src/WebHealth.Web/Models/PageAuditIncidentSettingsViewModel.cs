using System.ComponentModel.DataAnnotations;
using WebHealth.Application.PageAudits;

namespace WebHealth.Web.Models;

public sealed class PageAuditIncidentSettingsViewModel
{
    public Guid EndpointId { get; set; }
    public string EndpointLabel { get; set; } = string.Empty;
    public bool IncidentsEnabled { get; set; }
    public bool PerformanceScoreEnabled { get; set; }

    [Range(0, 100)]
    public int PerformanceMinimumScore { get; set; }

    public bool AccessibilityScoreEnabled { get; set; }

    [Range(0, 100)]
    public int AccessibilityMinimumScore { get; set; }

    public bool BestPracticesScoreEnabled { get; set; }

    [Range(0, 100)]
    public int BestPracticesMinimumScore { get; set; }

    public bool SeoScoreEnabled { get; set; }

    [Range(0, 100)]
    public int SeoMinimumScore { get; set; }

    public bool FirstContentfulPaintEnabled { get; set; }

    [Range(typeof(decimal), "0", "600000")]
    public decimal FirstContentfulPaintMaximum { get; set; }

    public bool LargestContentfulPaintEnabled { get; set; }

    [Range(typeof(decimal), "0", "600000")]
    public decimal LargestContentfulPaintMaximum { get; set; }

    public bool TotalBlockingTimeEnabled { get; set; }

    [Range(typeof(decimal), "0", "600000")]
    public decimal TotalBlockingTimeMaximum { get; set; }

    public bool CumulativeLayoutShiftEnabled { get; set; }

    [Range(typeof(decimal), "0", "10")]
    public decimal CumulativeLayoutShiftMaximum { get; set; }

    public bool SpeedIndexEnabled { get; set; }

    [Range(typeof(decimal), "0", "600000")]
    public decimal SpeedIndexMaximum { get; set; }

    public long Version { get; set; }

    public UpdatePageAuditIncidentPolicy ToCommand() => new(
        IncidentsEnabled,
        PerformanceScoreEnabled,
        PerformanceMinimumScore,
        AccessibilityScoreEnabled,
        AccessibilityMinimumScore,
        BestPracticesScoreEnabled,
        BestPracticesMinimumScore,
        SeoScoreEnabled,
        SeoMinimumScore,
        FirstContentfulPaintEnabled,
        FirstContentfulPaintMaximum,
        LargestContentfulPaintEnabled,
        LargestContentfulPaintMaximum,
        TotalBlockingTimeEnabled,
        TotalBlockingTimeMaximum,
        CumulativeLayoutShiftEnabled,
        CumulativeLayoutShiftMaximum,
        SpeedIndexEnabled,
        SpeedIndexMaximum,
        Version);

    public static PageAuditIncidentSettingsViewModel From(
        PageAuditIncidentPolicy policy,
        string endpointLabel) => new()
        {
            EndpointId = policy.EndpointId,
            EndpointLabel = endpointLabel,
            IncidentsEnabled = policy.IncidentsEnabled,
            PerformanceScoreEnabled = policy.PerformanceScoreEnabled,
            PerformanceMinimumScore = policy.PerformanceMinimumScore,
            AccessibilityScoreEnabled = policy.AccessibilityScoreEnabled,
            AccessibilityMinimumScore = policy.AccessibilityMinimumScore,
            BestPracticesScoreEnabled = policy.BestPracticesScoreEnabled,
            BestPracticesMinimumScore = policy.BestPracticesMinimumScore,
            SeoScoreEnabled = policy.SeoScoreEnabled,
            SeoMinimumScore = policy.SeoMinimumScore,
            FirstContentfulPaintEnabled = policy.FirstContentfulPaintEnabled,
            FirstContentfulPaintMaximum = policy.FirstContentfulPaintMaximum,
            LargestContentfulPaintEnabled = policy.LargestContentfulPaintEnabled,
            LargestContentfulPaintMaximum = policy.LargestContentfulPaintMaximum,
            TotalBlockingTimeEnabled = policy.TotalBlockingTimeEnabled,
            TotalBlockingTimeMaximum = policy.TotalBlockingTimeMaximum,
            CumulativeLayoutShiftEnabled = policy.CumulativeLayoutShiftEnabled,
            CumulativeLayoutShiftMaximum = policy.CumulativeLayoutShiftMaximum,
            SpeedIndexEnabled = policy.SpeedIndexEnabled,
            SpeedIndexMaximum = policy.SpeedIndexMaximum,
            Version = policy.Version
        };
}
