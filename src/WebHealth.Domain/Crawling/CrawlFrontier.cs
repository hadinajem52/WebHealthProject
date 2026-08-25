namespace WebHealth.Domain.Crawling;

public enum CrawlVisitMode
{
    Follow = 0,

    CheckOnly = 1
}

public sealed record CrawlWorkItem(CrawlUrl Url, int Depth, CrawlVisitMode Mode);

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

    public int MaxPages { get; init; } = DefaultMaxPages;

    public int MaxCheckOnlyRequests { get; init; } = DefaultMaxPages * 2;

    public int MaxDepth { get; init; } = DefaultMaxDepth;

    public int MaxQueryVariantsPerPath { get; init; } = DefaultMaxQueryVariantsPerPath;
}

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

    public bool PageBudgetExhausted => _pagesAdmitted >= _limits.MaxPages;

    public bool TryDequeue(out CrawlWorkItem item) => _pending.TryDequeue(out item!);

    public CrawlAdmission Offer(CrawlUrl url, int depth)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!_seen.Add(url.Value)) return CrawlAdmission.Skipped(CrawlSkipReasons.AlreadySeen);

        var scope = Scope.Decide(url);

        var mode = scope == CrawlScopeDecision.Internal && depth <= _limits.MaxDepth
            ? CrawlVisitMode.Follow
            : CrawlVisitMode.CheckOnly;

        if (mode == CrawlVisitMode.Follow)
        {
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
