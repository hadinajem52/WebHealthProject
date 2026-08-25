namespace WebHealth.Application.Monitoring;

public sealed record PerformanceSampleContext(string MonitorSource, string ConfigurationFingerprint);

public sealed record ComparabilityAssessment(
    bool IsComparable,
    IReadOnlyList<string> MonitorSources,
    bool ConfigurationChanged,
    string? Warning);

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
