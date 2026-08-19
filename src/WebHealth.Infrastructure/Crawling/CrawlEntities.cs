using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Crawling;

/// <summary>
/// One crawl, with what 6.6 decided about how it ended. Status and stop reason are separate columns
/// because "it stopped" and "it covered the site" are different facts, and only
/// <c>FrontierExhausted</c> means the second (BR-L05, BR-L10).
/// </summary>
public sealed class CrawlRun
{
    public Guid Id { get; set; }

    /// <summary>
    /// The endpoint whose target authorization every request in the run was checked against. It is
    /// also what makes two runs comparable: a comparison is only meaningful between crawls of the
    /// same site.
    /// </summary>
    public Guid EndpointId { get; set; }

    public required string Status { get; set; }
    public required string StopReason { get; set; }

    /// <summary>The seeds as configured, for a report that has to explain what was crawled.</summary>
    public required string SeedUrls { get; set; }

    public int PagesFetched { get; set; }
    public int LinksRecorded { get; set; }

    /// <summary>BR-L02. Recorded either way: a granted override, or the reason it was refused.</summary>
    public bool RobotsOverrideGranted { get; set; }
    public string? RobotsOverrideRefusedBecause { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while the run is in flight, so an interrupted process leaves a visibly
    /// unfinished run rather than one that looks complete.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    public Endpoint Endpoint { get; set; } = null!;
    public ICollection<CrawlLinkResult> Links { get; set; } = [];
}

/// <summary>
/// One source-target pair in one run (BR-L06, BR-L07). The highest-volume row in this phase, so
/// every column a report filters on lives here rather than being reached through the run.
/// <para>
/// The URL hashes carry identity and the URL text carries evidence. Uniqueness is enforced over the
/// hashes because a btree entry cannot hold two full 2048-character URLs; a report that could only
/// show a hash would be useless, so the text is stored beside it.
/// </para>
/// </summary>
public sealed class CrawlLinkResult
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }

    /// <summary>Null for a seed, which no page linked to.</summary>
    public string? SourceUrl { get; set; }
    public byte[]? SourceUrlHash { get; set; }

    public required string TargetUrl { get; set; }
    public required byte[] TargetUrlHash { get; set; }

    public required string Classification { get; set; }
    public string? SkipReason { get; set; }
    public int? StatusCode { get; set; }
    public int RedirectCount { get; set; }
    public string? FinalUrl { get; set; }

    /// <summary>A filter column, on the row the filter reads. Deliberately not a join to the run.</summary>
    public bool IsInternal { get; set; }

    /// <summary>Depth of first discovery; -1 where the run stopped before assigning one.</summary>
    public int Depth { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public CrawlRun Run { get; set; } = null!;
}
