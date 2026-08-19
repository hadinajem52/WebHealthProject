namespace WebHealth.Domain.Crawling;

/// <summary>
/// One source-target pair with the target's observed result. The source is null for a seed, which
/// no page linked to.
/// </summary>
public sealed record CrawlEdge(
    string? SourceUrl,
    string TargetUrl,
    string Classification,
    int? StatusCode,
    int RedirectCount,
    string? FinalUrl,
    string? SkipReason,
    int? DurationMs);

/// <summary>
/// BR-L07. A target is fetched once, but it may be linked from many pages, and "which page contains
/// the broken link" is what makes the report actionable. This ledger holds the two apart: it
/// collects the sources that point at each target and emits one result per distinct source-target
/// pair once that target's outcome is known.
/// <para>
/// Order does not matter. A source discovered after its target was fetched emits immediately; a
/// target fetched after several sources pointed at it emits one result for each. Pairs are
/// deduplicated, so a page linking to the same broken URL five times contributes one result and one
/// affected page.
/// </para>
/// <para>
/// Not thread-safe by design: it is pure bookkeeping, and the execution loop that owns it already
/// holds a lock over the frontier it advances in the same step.
/// </para>
/// </summary>
public sealed class CrawlLinkLedger
{
    private readonly Dictionary<string, Resolution> _resolved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string?>> _sourcesByTarget = new(StringComparer.Ordinal);
    private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);

    private sealed record Resolution(
        string Classification, int? StatusCode, int RedirectCount, string? FinalUrl,
        string? SkipReason, int? DurationMs);

    /// <summary>
    /// Records that <paramref name="sourceUrl" /> links to <paramref name="targetUrl" />, and
    /// returns the edge if the target's outcome is already known.
    /// </summary>
    public IReadOnlyList<CrawlEdge> RecordDiscovery(string? sourceUrl, string targetUrl)
    {
        ArgumentNullException.ThrowIfNull(targetUrl);
        if (!_sourcesByTarget.TryGetValue(targetUrl, out var sources))
        {
            sources = [];
            _sourcesByTarget.Add(targetUrl, sources);
        }

        sources.Add(sourceUrl);
        return _resolved.TryGetValue(targetUrl, out var resolution)
            ? Emit(targetUrl, resolution, [sourceUrl])
            : [];
    }

    /// <summary>Records what happened to a target that was requested.</summary>
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

    /// <summary>Records a target that was deliberately never requested, with the reason.</summary>
    public IReadOnlyList<CrawlEdge> RecordSkip(string targetUrl, string skipReason)
    {
        ArgumentNullException.ThrowIfNull(skipReason);
        return Resolve(targetUrl, new(CrawlLinkClassifications.Skipped, null, 0, null, skipReason, null));
    }

    /// <summary>
    /// Every target that was discovered but never resolved, as <c>Unknown</c>. Called once when a
    /// run stops for any reason, including cancellation: a target the run never reached must be
    /// visible as unreached rather than disappearing from the report.
    /// </summary>
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

        // The first resolution wins. A target is requested once, so a second outcome would be a
        // redirect hop or a retry arriving late, and letting it overwrite would rewrite results
        // already handed to the sink.
        if (!_resolved.TryAdd(targetUrl, resolution)) return [];

        return Emit(targetUrl, resolution, _sourcesByTarget.GetValueOrDefault(targetUrl) ?? []);
    }

    private IReadOnlyList<CrawlEdge> Emit(
        string targetUrl,
        Resolution resolution,
        IReadOnlyList<string?> sources)
    {
        var edges = new List<CrawlEdge>();
        foreach (var source in sources)
        {
            if (!_emitted.Add($"{source}\n{targetUrl}")) continue;
            edges.Add(new(source, targetUrl, resolution.Classification, resolution.StatusCode,
                resolution.RedirectCount, resolution.FinalUrl, resolution.SkipReason,
                resolution.DurationMs));
        }

        return edges;
    }
}
