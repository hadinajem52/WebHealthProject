namespace WebHealth.Domain.Crawling;

public enum CrawlScopeDecision
{
    /// <summary>An allowed host and an allowed path prefix: fetch it, and follow its links.</summary>
    Internal = 0,

    /// <summary>A canonical target outside scope: check its status, never follow it (BR-L08).</summary>
    /// <remarks>
    /// There is no third case here. A URL that is not a crawl target at all is rejected by
    /// <see cref="CrawlUrlNormalizer" /> before scope is ever consulted, so everything this enum
    /// describes is already a canonical http(s) URL.
    /// </remarks>
    External = 1
}

/// <summary>
/// One allowed host. Subdomains are opt-in per host rather than implied: a crawl configured for
/// <c>example.com</c> that silently swept <c>anything.example.com</c> would leave the scope its
/// operator chose, and on a site with user-controlled subdomains that is unbounded.
/// </summary>
public sealed record CrawlHostRule(string Host, bool IncludeSubdomains = false)
{
    public bool Matches(string candidateHost)
    {
        ArgumentNullException.ThrowIfNull(candidateHost);
        if (string.Equals(candidateHost, Host, StringComparison.Ordinal)) return true;
        return IncludeSubdomains
            && candidateHost.EndsWith($".{Host}", StringComparison.Ordinal);
    }
}

/// <summary>
/// BR-L01. Where a crawl may go, as a pure decision over a canonical URL. Seeds are held here too
/// because a seed outside its own scope is a configuration error worth catching at validation
/// rather than discovering halfway through a run.
/// </summary>
public sealed record CrawlScope(
    IReadOnlyList<CrawlUrl> Seeds,
    IReadOnlyList<CrawlHostRule> AllowedHosts,
    IReadOnlyList<string> AllowedPathPrefixes)
{
    /// <summary>
    /// Scope derived entirely from the seeds: their hosts, and their own directories as prefixes.
    /// This is the conservative default — a crawl seeded at <c>/docs/index.html</c> stays under
    /// <c>/docs/</c> rather than discovering the whole host.
    /// </summary>
    public static CrawlScope FromSeeds(IReadOnlyList<CrawlUrl> seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        return new(
            seeds,
            [.. seeds.Select(seed => seed.Host).Distinct(StringComparer.Ordinal)
                .Select(host => new CrawlHostRule(host))],
            [.. seeds.Select(seed => seed.Directory).Distinct(StringComparer.Ordinal)]);
    }

    public CrawlScopeDecision Decide(CrawlUrl url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return AllowedHosts.Any(rule => rule.Matches(url.Host)) && IsPathAllowed(url.Path)
            ? CrawlScopeDecision.Internal
            : CrawlScopeDecision.External;
    }

    /// <summary>
    /// An empty prefix list means the whole host. Matching is case-sensitive because path case is
    /// preserved by URL identity, and a scope that folded case would admit paths the operator's
    /// configuration does not name.
    /// </summary>
    private bool IsPathAllowed(string path) =>
        AllowedPathPrefixes.Count == 0
        || AllowedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// Validation errors for a run's scope, so an unusable crawl is refused before it makes a
    /// single request. An empty result means the scope is usable.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Seeds.Count == 0) errors.Add("A crawl needs at least one seed URL.");
        if (AllowedHosts.Count == 0) errors.Add("A crawl needs at least one allowed host.");

        foreach (var seed in Seeds.Where(seed => Decide(seed) != CrawlScopeDecision.Internal))
        {
            errors.Add($"The seed {seed.Value} is outside the crawl's allowed hosts and path prefixes.");
        }

        return errors;
    }
}
