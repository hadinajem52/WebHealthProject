namespace WebHealth.Domain.Crawling;

public enum CrawlVisitMode
{
    /// <summary>Fetch it, read its links, and queue what it points at.</summary>
    Follow = 0,

    /// <summary>Fetch it for a status only. External links, and internal links past the depth
    /// limit, are checked but never explored (BR-L08).</summary>
    CheckOnly = 1
}

public sealed record CrawlWorkItem(CrawlUrl Url, int Depth, CrawlVisitMode Mode);

/// <summary>
/// The result of offering one URL to the frontier. <c>Admitted</c> false always carries a reason,
/// so a caller cannot record a skip without saying why it happened.
/// </summary>
public readonly record struct CrawlAdmission(bool Admitted, CrawlVisitMode Mode, string? SkipReason)
{
    public static CrawlAdmission Skipped(string reason) => new(false, CrawlVisitMode.CheckOnly, reason);

    public static CrawlAdmission Accepted(CrawlVisitMode mode) => new(true, mode, null);
}

public sealed record CrawlLimits
{
    public const int DefaultMaxPages = 1000;
    public const int DefaultMaxDepth = 5;
    public const int DefaultMaxQueryVariantsPerPath = 32;

    public static CrawlLimits Default { get; } = new();

    /// <summary>How many internal pages may be fetched and followed.</summary>
    public int MaxPages { get; init; } = DefaultMaxPages;

    /// <summary>
    /// How many status-only checks may be made. Separate from the page budget on purpose: one page
    /// carrying ten thousand outbound links would otherwise make ten thousand requests inside a
    /// budget of one page.
    /// </summary>
    public int MaxCheckOnlyRequests { get; init; } = DefaultMaxPages * 2;

    /// <summary>Depth 0 is a seed. The limit bounds what is followed, not what is checked.</summary>
    public int MaxDepth { get; init; } = DefaultMaxDepth;

    /// <summary>Section 2.2: bounds a faceted section without cutting off pagination.</summary>
    public int MaxQueryVariantsPerPath { get; init; } = DefaultMaxQueryVariantsPerPath;
}

/// <summary>
/// BR-L03 and BR-L05. Revisit prevention, depth, the page budget and the query-variant cap, as one
/// deterministic state machine with no I/O and no clock. Every decision that bounds a crawl lives
/// here so it can be driven to its limits by a unit test rather than by a live site.
/// <para>
/// The queue is FIFO, which makes the traversal breadth-first, which makes the depth recorded for a
/// URL the shortest path to it from a seed. That is why depth is taken from the first admission and
/// never revised: a later, deeper rediscovery would otherwise push a shallow page past the depth
/// limit and drop it.
/// </para>
/// </summary>
public sealed class CrawlFrontier
{
    private readonly CrawlLimits _limits;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _queryVariantsByPath = new(StringComparer.Ordinal);
    private readonly Queue<CrawlWorkItem> _pending = new();

    private int _pagesAdmitted;
    private int _checksAdmitted;

    public CrawlFrontier(CrawlScope scope, CrawlLimits limits)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(limits);
        Scope = scope;
        _limits = limits;
        foreach (var seed in scope.Seeds)
        {
            Offer(seed, depth: 0);
        }
    }

    public CrawlScope Scope { get; }

    public int PagesAdmitted => _pagesAdmitted;

    public int CheckOnlyRequestsAdmitted => _checksAdmitted;

    public bool HasWork => _pending.Count > 0;

    /// <summary>
    /// True once no further page could be admitted however many links arrive. 6.6 reports this as
    /// the stop reason instead of <c>FrontierExhausted</c>, because a crawl that hit its budget has
    /// not covered the site and must not claim it did.
    /// </summary>
    public bool PageBudgetExhausted => _pagesAdmitted >= _limits.MaxPages;

    public bool TryDequeue(out CrawlWorkItem item) => _pending.TryDequeue(out item!);

    /// <summary>
    /// Offers a canonical URL discovered at <paramref name="depth" />. Admission is idempotent: the
    /// same URL offered again is skipped as already seen, whatever depth or page it came from.
    /// </summary>
    public CrawlAdmission Offer(CrawlUrl url, int depth)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!_seen.Add(url.Value)) return CrawlAdmission.Skipped(CrawlSkipReasons.AlreadySeen);

        var scope = Scope.Decide(url);

        // An internal page past the depth limit is still worth a status: a broken link at depth six
        // is a broken link. It just contributes none of its own links.
        var mode = scope == CrawlScopeDecision.Internal && depth <= _limits.MaxDepth
            ? CrawlVisitMode.Follow
            : CrawlVisitMode.CheckOnly;

        if (mode == CrawlVisitMode.Follow)
        {
            // Budget before reservation: a URL refused for the page limit must not also consume one
            // of its path's query-variant slots, or the cap would drift down as the budget runs out.
            if (PageBudgetExhausted) return CrawlAdmission.Skipped(CrawlSkipReasons.PageLimit);
            if (!TryReserveQueryVariant(url)) return CrawlAdmission.Skipped(CrawlSkipReasons.QueryVariantCap);
            _pagesAdmitted++;
        }
        else
        {
            if (_checksAdmitted >= _limits.MaxCheckOnlyRequests)
            {
                return CrawlAdmission.Skipped(CrawlSkipReasons.ExternalCheckLimit);
            }

            _checksAdmitted++;
        }

        _pending.Enqueue(new(url, depth, mode));
        return CrawlAdmission.Accepted(mode);
    }

    /// <summary>
    /// The query-variant cap counts distinct canonical URLs sharing one path. A URL with no query
    /// is the path's single canonical form and is never capped — capping it would refuse the one
    /// page the section is actually named by.
    /// </summary>
    private bool TryReserveQueryVariant(CrawlUrl url)
    {
        if (!url.HasQuery) return true;

        var key = $"{url.Origin}{url.Path}";
        var used = _queryVariantsByPath.GetValueOrDefault(key);
        if (used >= _limits.MaxQueryVariantsPerPath) return false;

        _queryVariantsByPath[key] = used + 1;
        return true;
    }
}
