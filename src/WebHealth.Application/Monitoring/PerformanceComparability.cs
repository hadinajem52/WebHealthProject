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
        var sources = contexts
            .Select(sample => sample.MonitorSource)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var configurationChanged = contexts
            .Select(sample => sample.ConfigurationFingerprint)
            .Distinct(StringComparer.Ordinal)
            .Count() > 1;

        if (sources.Length <= 1 && !configurationChanged)
        {
            return new(true, sources, false, null);
        }

        return new(false, sources, configurationChanged, DescribeWarning(sources, configurationChanged));
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
