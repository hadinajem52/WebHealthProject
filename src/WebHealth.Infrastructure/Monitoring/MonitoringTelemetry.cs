using System.Diagnostics;
using System.Diagnostics.Metrics;
using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

internal static class MonitoringTelemetry
{
    internal const string MeterName = "WebHealth.Monitoring";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Operations = Meter.CreateCounter<long>("monitoring.operations", "{operation}");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("monitoring.duration", "ms");
    private static readonly HashSet<string> FailureCategories =
        ["None", "Database", "Cancellation", "QueueEnqueue", "Unexpected", .. Enum.GetNames<SafeHttpFailureKind>(), .. Enum.GetNames<SslProbeFailureKind>()];

    public static void Record(string monitorType, string source, string operation, string? failureCategory, double durationMs)
    {
        var tags = new TagList
        {
            { "monitor_type", monitorType is "HttpAvailability" or "SslCertificate" ? monitorType : "Unknown" },
            { "source", source is "Scheduled" or "Manual" or "Urgent" ? source : "Unknown" },
            { "operation", operation is "transport" or "monitoring-dispatch" or "monitoring-reconciliation" ? operation : "Unknown" },
            { "failure_category", failureCategory is null ? "None" : FailureCategories.Contains(failureCategory) ? failureCategory : "Unknown" }
        };
        Operations.Add(1, tags);
        Duration.Record(Math.Max(0, durationMs), tags);
    }
}
