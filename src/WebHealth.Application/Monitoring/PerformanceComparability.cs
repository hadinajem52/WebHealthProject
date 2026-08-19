namespace WebHealth.Application.Monitoring;

/// <summary>
/// One stored result's measurement context: what measured it, and the configuration it was
/// measured under.
/// </summary>
public sealed record PerformanceSampleContext(string MonitorSource, string ConfigurationFingerprint);

/// <summary>
/// Whether a set of results may be compared to each other, and why not when they may not.
/// </summary>
public sealed record ComparabilityAssessment(
    bool IsComparable,
    IReadOnlyList<string> MonitorSources,
    bool ConfigurationChanged,
    string? Warning);

/// <summary>
/// BR-P05. Response times only mean the same thing when the same monitor measured them under
/// the same configuration: a certificate probe's duration and an HTTP check's duration are not
/// the same quantity, and neither are two HTTP checks whose timeout or redirect budget changed
/// between them. Rather than silently dropping or blending such samples, the reports keep them
/// and say what changed — a set with a gap in it is more useful than a set that quietly
/// omitted half its history.
/// </summary>
public static class PerformanceComparability
{
    public static ComparabilityAssessment Evaluate(IEnumerable<PerformanceSampleContext> samples)
    {
        var contexts = samples.ToArray();
        return Evaluate(
            contexts.Select(sample => sample.MonitorSource),
            contexts
                .Select(sample => sample.ConfigurationFingerprint)
                .Distinct(StringComparer.Ordinal)
                .Count() > 1);
    }

    /// <summary>
    /// The two facts the rule actually turns on: which monitors produced the samples, and
    /// whether the configuration changed at all across them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stating it this way is what lets a caller answer the question without materialising one
    /// context per sample: the distinct sources are a handful of values, and whether the
    /// configuration changed is a single boolean.
    /// </para>
    /// <para>
    /// <paramref name="configurationChanged" /> means <em>a monitor's own configuration changed
    /// during the period</em>. It is not a comparison between monitors. A fingerprint hashes the
    /// endpoint's normalized URL among other things, so two monitors always have different
    /// fingerprints, and a caller that compares them across a fleet is asking "are these
    /// different monitors?" - always yes, and the warning is then permanently on.
    /// </para>
    /// </remarks>
    public static ComparabilityAssessment Evaluate(
        IEnumerable<string> monitorSources,
        bool configurationChanged)
    {
        var sources = monitorSources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        return sources.Length <= 1 && !configurationChanged
            ? new(true, sources, false, null)
            : new(false, sources, configurationChanged, DescribeWarning(sources, configurationChanged));
    }

    private static string DescribeWarning(IReadOnlyList<string> sources, bool configurationChanged)
    {
        if (sources.Count > 1 && configurationChanged)
        {
            return "These results were produced by more than one monitor and under more than one "
                + "check configuration, so their timings may not be comparable.";
        }

        return sources.Count > 1
            ? "These results were produced by more than one monitor, so their timings may not be comparable."
            : "The check configuration changed across these results, so their timings may not be comparable.";
    }
}
