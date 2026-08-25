using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Crawling;

public sealed class CrawlRun
{
    public Guid Id { get; set; }

    public Guid EndpointId { get; set; }

    public required string Status { get; set; }
    public required string StopReason { get; set; }

    public required string SeedUrls { get; set; }

    public string? AllowedHosts { get; set; }
    public string? AllowedPathPrefixes { get; set; }
    public required string QueryPolicy { get; set; }
    public int MaxPages { get; set; }
    public int MaxDepth { get; set; }
    public bool CheckExternalLinks { get; set; }

    public string? FailureReason { get; set; }

    public bool CoverageLimited { get; set; }

    public int PagesFetched { get; set; }
    public int LinksRecorded { get; set; }

    public bool RobotsOverrideGranted { get; set; }
    public string? RobotsOverrideRefusedBecause { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public Guid? ExecutionClaimId { get; set; }

    public Endpoint Endpoint { get; set; } = null!;
    public ICollection<CrawlLinkResult> Links { get; set; } = [];
}

public sealed class CrawlLinkResult
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }

    public string? SourceUrl { get; set; }
    public byte[]? SourceUrlHash { get; set; }

    public required string TargetUrl { get; set; }
    public required byte[] TargetUrlHash { get; set; }

    public required string Classification { get; set; }
    public string? SkipReason { get; set; }
    public int? StatusCode { get; set; }
    public int RedirectCount { get; set; }
    public string? FinalUrl { get; set; }

    public bool IsInternal { get; set; }

    public int Depth { get; set; }

    public int? DurationMs { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public CrawlRun Run { get; set; } = null!;
}
