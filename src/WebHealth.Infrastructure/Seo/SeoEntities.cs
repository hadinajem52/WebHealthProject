using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Seo;

/// <summary>
/// One SEO decision, keyed to the logical check that produced it. Holds extracted values and their
/// observed lengths only (BR-E10): there is no column that can hold markup, so the HTML cannot be
/// retained here even by mistake.
/// </summary>
public sealed class SeoObservation
{
    public Guid LogicalCheckId { get; set; }
    public Guid EndpointMonitorId { get; set; }

    /// <summary>BR-E01: <c>Applicable</c>, or <c>NotApplicable</c> with a recorded reason.</summary>
    public required string Applicability { get; set; }
    public string? NotApplicableReason { get; set; }

    /// <summary>
    /// The body hit the response cap. Presence-based rules stay valid; an absence-based rule must
    /// not fire from a document that was cut short.
    /// </summary>
    public bool DocumentTruncated { get; set; }

    public string? Title { get; set; }
    public int TitleLength { get; set; }
    public int TitleCount { get; set; }
    public string? MetaDescription { get; set; }
    public int MetaDescriptionLength { get; set; }
    public int MetaDescriptionCount { get; set; }
    public string? CanonicalHref { get; set; }
    public int CanonicalLength { get; set; }
    public int CanonicalCount { get; set; }
    public string? CanonicalAbsoluteUrl { get; set; }
    public string? RobotsMeta { get; set; }
    public int RobotsMetaLength { get; set; }
    public int RobotsMetaCount { get; set; }
    public DateTimeOffset ObservedAt { get; set; }

    public LogicalCheck LogicalCheck { get; set; } = null!;
    public EndpointMonitor EndpointMonitor { get; set; } = null!;
}
