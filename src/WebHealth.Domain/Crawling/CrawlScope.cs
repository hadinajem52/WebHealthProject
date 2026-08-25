namespace WebHealth.Domain.Crawling;

public enum CrawlScopeDecision
{
    Internal = 0,

    External = 1
}

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

public sealed record CrawlScope(
    IReadOnlyList<CrawlUrl> Seeds,
    IReadOnlyList<CrawlHostRule> AllowedHosts,
    IReadOnlyList<string> AllowedPathPrefixes)
{
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

    private bool IsPathAllowed(string path) =>
        AllowedPathPrefixes.Count == 0
        || AllowedPathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal));

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
