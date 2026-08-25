using WebHealth.Application.Seo;

namespace WebHealth.Infrastructure.Seo;

public sealed class RobotsSnapshot
{
    public required string Origin { get; set; }

    public required string Host { get; set; }
    public int Port { get; set; }
    public required string Status { get; set; }
    public int? HttpStatus { get; set; }

    public string? Content { get; set; }

    public bool SitemapRequired { get; set; }
    public string? ConfiguredSitemapUrl { get; set; }
    public string? CheckedSitemapUrl { get; set; }
    public int? SitemapHttpStatus { get; set; }
    public bool SitemapAvailable { get; set; }

    public string? ExceptionReason { get; set; }
    public Guid? ExceptionApprovedByUserId { get; set; }
    public DateTimeOffset? ExceptionApprovedAt { get; set; }

    public long Version { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public RobotsSnapshotFacts ToFacts() => new(
        Status, Content, ExceptionReason is not null, SitemapRequired, SitemapAvailable);
}
