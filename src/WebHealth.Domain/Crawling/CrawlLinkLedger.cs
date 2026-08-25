namespace WebHealth.Domain.Crawling;

public sealed record CrawlEdge(
    string? SourceUrl,
    string TargetUrl,
    string Classification,
    int? StatusCode,
    int RedirectCount,
    string? FinalUrl,
    string? SkipReason,
    int? DurationMs);

public sealed class CrawlLinkLedger
{
    private readonly Dictionary<string, Resolution> _resolved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string?>> _sourcesByTarget = new(StringComparer.Ordinal);
    private readonly HashSet<(string? Source, string Target)> _emitted = [];

    private sealed record Resolution(
        string Classification, int? StatusCode, int RedirectCount, string? FinalUrl,
        string? SkipReason, int? DurationMs);

    public IReadOnlyList<CrawlEdge> RecordDiscovery(string? sourceUrl, string targetUrl)
    {
        ArgumentNullException.ThrowIfNull(targetUrl);
        if (!_sourcesByTarget.TryGetValue(targetUrl, out var sources))
        {
            sources = new(StringComparer.Ordinal);
            _sourcesByTarget.Add(targetUrl, sources);
        }

        if (!sources.Add(sourceUrl)) return [];
        return _resolved.TryGetValue(targetUrl, out var resolution)
            ? Emit(targetUrl, resolution, new HashSet<string?>([sourceUrl], StringComparer.Ordinal))
            : [];
    }

    public IReadOnlyList<CrawlEdge> RecordOutcome(
        string targetUrl,
        CrawlRequestObservation observation,
        string? finalUrl = null,
        int? durationMs = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return Resolve(targetUrl, new(
            CrawlLinkClassifier.Classify(observation),
            observation.StatusCode,
            observation.RedirectCount,
            finalUrl,
            null,
            durationMs));
    }

    public IReadOnlyList<CrawlEdge> RecordSkip(string targetUrl, string skipReason)
    {
        ArgumentNullException.ThrowIfNull(skipReason);
        return Resolve(targetUrl, new(CrawlLinkClassifications.Skipped, null, 0, null, skipReason, null));
    }

    public IReadOnlyList<CrawlEdge> Flush()
    {
        var edges = new List<CrawlEdge>();
        foreach (var target in _sourcesByTarget.Keys.Where(target => !_resolved.ContainsKey(target)).ToArray())
        {
            edges.AddRange(Resolve(target, new(
                CrawlLinkClassifications.Unknown, null, 0, null, CrawlSkipReasons.RunStopped, null)));
        }

        return edges;
    }

    private IReadOnlyList<CrawlEdge> Resolve(string targetUrl, Resolution resolution)
    {
        ArgumentNullException.ThrowIfNull(targetUrl);

        if (!_resolved.TryAdd(targetUrl, resolution)) return [];

        return Emit(
            targetUrl,
            resolution,
            _sourcesByTarget.GetValueOrDefault(targetUrl)
                ?? new HashSet<string?>(StringComparer.Ordinal));
    }

    private IReadOnlyList<CrawlEdge> Emit(
        string targetUrl,
        Resolution resolution,
        IReadOnlySet<string?> sources)
    {
        var edges = new List<CrawlEdge>();
        foreach (var source in sources)
        {
            if (!_emitted.Add((source, targetUrl))) continue;
            edges.Add(new(source, targetUrl, resolution.Classification, resolution.StatusCode,
                resolution.RedirectCount, resolution.FinalUrl, resolution.SkipReason,
                resolution.DurationMs));
        }

        return edges;
    }
}
