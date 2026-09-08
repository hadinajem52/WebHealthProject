using Npgsql;
using WebHealth.Application.Reporting;

namespace WebHealth.Infrastructure.Reporting;

internal sealed class ReportSampleAggregate
{
    public static ReportSampleAggregate Empty => new();
    public long EligibleSamples { get; private set; }
    public long HealthySamples { get; private set; }
    public long WarningSamples { get; private set; }
    public long DownSamples { get; private set; }
    public long ExcludedSamples { get; private set; }
    public long RespondedSamples { get; private set; }
    public DateTimeOffset? LastMeasuredAt { get; private set; }
    private string? lowestSource;
    private string? highestSource;
    private readonly long[] histogram = new long[ResponseTimeHistogram.BucketCount];
    private int maximumDuration;
    private double? exactP50;
    private double? exactP95;
    private bool approximate;
    private long rawSamples;
    private long aggregatedSamples;
    private bool partialArchivedDaysOmitted;

    public string? SingleMonitorSource => string.Equals(lowestSource, highestSource, StringComparison.Ordinal) ? lowestSource : null;

    public void Add(NpgsqlDataReader reader)
    {
        checked
        {
            EligibleSamples += reader.GetInt64(1);
            HealthySamples += reader.GetInt64(2);
            WarningSamples += reader.GetInt64(3);
            DownSamples += reader.GetInt64(4);
            ExcludedSamples += reader.GetInt64(5);
            RespondedSamples += reader.GetInt64(6);
            var buckets = reader.GetFieldValue<long[]>(12);
            for (var index = 0; index < histogram.Length; index++) histogram[index] += buckets[index];
            rawSamples += reader.GetInt64(14);
            aggregatedSamples += reader.GetInt64(15);
        }
        if (reader.GetInt64(14) > 0)
        {
            exactP50 = reader.IsDBNull(7) ? null : reader.GetDouble(7);
            exactP95 = reader.IsDBNull(8) ? null : reader.GetDouble(8);
        }
        approximate |= reader.GetInt64(15) > 0 && reader.GetInt64(6) > 0;
        if (!reader.IsDBNull(9))
        {
            var measured = reader.GetFieldValue<DateTimeOffset>(9);
            if (LastMeasuredAt is null || measured > LastMeasuredAt) LastMeasuredAt = measured;
        }
        if (!reader.IsDBNull(10))
        {
            var source = reader.GetString(10);
            if (lowestSource is null || string.CompareOrdinal(source, lowestSource) < 0) lowestSource = source;
        }
        if (!reader.IsDBNull(11))
        {
            var source = reader.GetString(11);
            if (highestSource is null || string.CompareOrdinal(source, highestSource) > 0) highestSource = source;
        }
        if (!reader.IsDBNull(13)) maximumDuration = Math.Max(maximumDuration, reader.GetInt32(13));
        partialArchivedDaysOmitted |= reader.GetBoolean(16);
    }

    public ReportUptime ToUptime() => new(EligibleSamples, HealthySamples, WarningSamples, DownSamples, ExcludedSamples);
    public ReportHistoryCoverage ToHistory() => new(rawSamples, aggregatedSamples, partialArchivedDaysOmitted);
    public ReportResponseTimes ToResponseTimes() => new(
        approximate ? ResponseTimeHistogram.EstimatePercentile(histogram, 0.5, maximumDuration) : exactP50,
        approximate ? ResponseTimeHistogram.EstimatePercentile(histogram, 0.95, maximumDuration) : exactP95,
        RespondedSamples, approximate);
}
