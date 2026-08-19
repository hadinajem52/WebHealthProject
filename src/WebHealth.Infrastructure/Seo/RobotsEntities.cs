using WebHealth.Application.Seo;

namespace WebHealth.Infrastructure.Seo;

/// <summary>
/// One <c>robots.txt</c> per origin (BR-E06), not per endpoint. Fifty endpoints on one host share
/// this row, which is the whole point: they must produce one fetch, not fifty.
/// </summary>
public sealed class RobotsSnapshot
{
    /// <summary>Scheme, host and effective port — the cache key, and the natural key.</summary>
    public required string Origin { get; set; }

    public required string Host { get; set; }
    public int Port { get; set; }
    public required string Status { get; set; }
    public int? HttpStatus { get; set; }

    /// <summary>
    /// The fetched text, bounded. robots.txt is a public policy document rather than page content,
    /// so retaining it does not touch BR-E10 — but a column fed by a remote host is bounded anyway.
    /// </summary>
    public string? Content { get; set; }

    public bool SitemapRequired { get; set; }
    public string? ConfiguredSitemapUrl { get; set; }
    public string? CheckedSitemapUrl { get; set; }
    public int? SitemapHttpStatus { get; set; }
    public bool SitemapAvailable { get; set; }

    /// <summary>
    /// BR-E07: an origin may legitimately be blocked. The exception carries a reason and an
    /// approver, the same shape the endpoint's HTTP exception uses — never a silent flag.
    /// </summary>
    public string? ExceptionReason { get; set; }
    public Guid? ExceptionApprovedByUserId { get; set; }
    public DateTimeOffset? ExceptionApprovedAt { get; set; }

    /// <summary>Optimistic concurrency for the operator-set policy fields.</summary>
    public long Version { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public RobotsSnapshotFacts ToFacts() => new(
        Status, Content, ExceptionReason is not null, SitemapRequired, SitemapAvailable);
}
