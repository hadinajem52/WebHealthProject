using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Seo;

public sealed class SeoObservation
{
    public Guid LogicalCheckId { get; set; }
    public Guid EndpointMonitorId { get; set; }

    public required string Applicability { get; set; }
    public string? NotApplicableReason { get; set; }

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

    public string? PolicyExpectedHost { get; set; }
    public string? PolicyIndexingExpectation { get; set; }
    public bool PolicyDescriptionRequired { get; set; }

    public DateTimeOffset ObservedAt { get; set; }

    public LogicalCheck LogicalCheck { get; set; } = null!;
    public EndpointMonitor EndpointMonitor { get; set; } = null!;
}
