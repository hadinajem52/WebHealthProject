using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentAssertions;
using WebHealth.Infrastructure.Monitoring;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class MonitoringTelemetryTests
{
    [Fact]
    public void MetricDimensionsCannotContainArbitraryIdentifiersOrTargetData()
    {
        var recorded = new ConcurrentBag<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, activeListener) =>
        {
            if (instrument.Meter.Name == MonitoringTelemetry.MeterName)
                activeListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => recorded.Add(tags.ToArray()));
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) => recorded.Add(tags.ToArray()));
        listener.Start();
        foreach (var untrusted in new[] { "https://private.test/?token=secret", Guid.NewGuid().ToString(), "<certificate bytes>" })
            MonitoringTelemetry.Record(untrusted, untrusted, untrusted, untrusted, 12);

        recorded.Should().HaveCount(6);
        recorded.Should().AllSatisfy(tags =>
        {
            tags.Select(tag => tag.Key).Should().BeEquivalentTo("monitor_type", "source", "operation", "failure_category");
            tags.Select(tag => tag.Value).Should().OnlyContain(value => Equals(value, "Unknown"));
        });
        recorded.Clear();
        MonitoringTelemetry.Record("SslCertificate", "Urgent", "transport", "Unexpected", 12);
        recorded.Should().HaveCount(2);
        recorded.Should().AllSatisfy(tags => tags.ToDictionary(tag => tag.Key, tag => tag.Value)
            .Should().BeEquivalentTo(new Dictionary<string, object?>
            {
                ["monitor_type"] = "SslCertificate",
                ["source"] = "Urgent",
                ["operation"] = "transport",
                ["failure_category"] = "Unexpected"
            }));
    }
}
