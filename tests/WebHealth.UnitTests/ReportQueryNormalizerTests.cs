using FluentAssertions;
using WebHealth.Application.Reporting;
using WebHealth.Domain.Health;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// Every reporting bound is applied here, server-side. These tests are the record that a
/// hand-written request cannot widen the window, skip validation or page past the end.
/// </summary>
public sealed class ReportQueryNormalizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnEmptyRequestGetsABoundedDefaultWindowEndingNow()
    {
        var query = Normalize(new ReportQueryInput()).Query.Should().NotBeNull().And.Subject.As<ReportQuery>();

        query.WindowEnd.Should().Be(Now);
        query.WindowStart.Should().Be(Now.AddDays(-ReportQueryNormalizer.DefaultWindowDays));
        query.PageSize.Should().Be(ReportQueryNormalizer.ScreenPageSize);
        query.Page.Should().Be(1);
    }

    [Fact]
    public void TheWindowIsResolvedToUtcWhateverOffsetTheRequestCarried()
    {
        // BR-U04 is about instants, not about wall clocks: the same moment expressed in two
        // offsets has to select the same samples.
        var utc = Normalize(new ReportQueryInput(
            WindowStart: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            WindowEnd: new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero))).Query!;
        var offset = Normalize(new ReportQueryInput(
            WindowStart: new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.FromHours(2)),
            WindowEnd: new DateTimeOffset(2026, 8, 2, 2, 0, 0, TimeSpan.FromHours(2)))).Query!;

        offset.WindowStart.Should().Be(utc.WindowStart);
        offset.WindowEnd.Should().Be(utc.WindowEnd);
        utc.WindowStart.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AWindowThatDoesNotEndAfterItStartsIsRejected(int lengthHours)
    {
        var start = Now.AddDays(-1);

        var result = Normalize(new ReportQueryInput(
            WindowStart: start, WindowEnd: start.AddHours(lengthHours)));

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainSingle();
    }

    [Fact]
    public void AWindowLongerThanTheMaximumIsRejected()
    {
        var end = Now;
        var tooLong = Normalize(new ReportQueryInput(
            WindowStart: end.AddDays(-(ReportQueryNormalizer.MaximumWindowDays + 1)), WindowEnd: end));
        var atTheLimit = Normalize(new ReportQueryInput(
            WindowStart: end.AddDays(-ReportQueryNormalizer.MaximumWindowDays), WindowEnd: end));

        tooLong.Succeeded.Should().BeFalse();
        atTheLimit.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public void APageBelowOneIsClampedRatherThanRejected(int page)
    {
        // A nonsensical page is a navigation slip, not an attack; clamping keeps the report
        // usable while still refusing to compute a negative offset.
        Normalize(new ReportQueryInput(Page: page)).Query!.Page.Should().Be(1);
    }

    [Theory]
    [InlineData("Healthy")]
    [InlineData("Warning")]
    [InlineData("Critical")]
    [InlineData("Unknown")]
    [InlineData("Disabled")]
    public void EverySelectableHealthStatusIsAccepted(string status)
    {
        Normalize(new ReportQueryInput(HealthStatus: status)).Query!.HealthStatus.Should().Be(status);
    }

    /// <summary>
    /// Disabled was previously rejected as "a registry state rather than a monitoring outcome".
    /// That held while the dashboard never reported it — but a disabled monitor now reports
    /// Disabled instead of the state it was in when checking stopped, so it is a bucket a reader
    /// can see. A status that appears on the page and cannot be filtered for is the contradiction
    /// this test now guards against.
    /// </summary>
    [Fact]
    public void ADisabledMonitorCanBeFilteredForBecauseItIsReported()
    {
        var result = Normalize(new ReportQueryInput(HealthStatus: EndpointHealthStatuses.Disabled));

        result.Succeeded.Should().BeTrue(string.Join(" ", result.Errors));
        result.Query!.HealthStatus.Should().Be(EndpointHealthStatuses.Disabled);
    }

    [Fact]
    public void AnUnknownMonitorTypeIsRejected()
    {
        Normalize(new ReportQueryInput(MonitorType: "SmokeSignal")).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void BlankFilterValuesAreTreatedAsAbsentRatherThanInvalid()
    {
        // Empty query-string parameters arrive as "" from a form that submits every field.
        var query = Normalize(new ReportQueryInput(HealthStatus: "  ", MonitorType: "")).Query!;

        query.HealthStatus.Should().BeNull();
        query.MonitorType.Should().BeNull();
    }

    [Fact]
    public void EveryValidationFailureIsReportedTogether()
    {
        var result = Normalize(new ReportQueryInput(
            HealthStatus: "Nonsense",
            MonitorType: "Nonsense",
            WindowStart: Now,
            WindowEnd: Now.AddDays(-1)));

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().HaveCount(3);
    }

    [Fact]
    public void ExportingKeepsEveryFilterAndOnlyChangesTheSlice()
    {
        // This is what keeps the export honest: it re-slices the screen's query rather than
        // building its own.
        var query = Normalize(new ReportQueryInput(
            ClientId: Guid.NewGuid(),
            OwnerSubjectId: Guid.NewGuid(),
            EnvironmentId: Guid.NewGuid(),
            MonitorType: "HttpAvailability",
            HealthStatus: EndpointHealthStatuses.Critical,
            Page: 3)).Query!;

        var exportQuery = query.ForExport();

        exportQuery.Should().BeEquivalentTo(
            query,
            options => options.Excluding(candidate => candidate.PageSize)
                .Excluding(candidate => candidate.Page));
        exportQuery.PageSize.Should().Be(ReportQueryNormalizer.MaximumMonitors);
    }

    [Fact]
    public void ExportingAlwaysStartsAtTheFirstPage()
    {
        // The export is the whole filtered set. Carrying the screen's page over would produce a
        // file starting at row 5,001 that still looked like the complete answer.
        var query = Normalize(new ReportQueryInput(Page: 7)).Query!;

        query.Page.Should().Be(7);
        query.ForExport().Page.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void RepagingCannotEscapeThePageBound(int page)
    {
        Normalize(new ReportQueryInput()).Query!.WithPaging(page).Page.Should().Be(1);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(ReportQueryNormalizer.MaximumMonitors + 1, ReportQueryNormalizer.MaximumMonitors)]
    public void RepagingCannotEscapeThePageSizeBound(int pageSize, int expected)
    {
        // The filter object owns its invariants: a caller cannot produce a zero page size that
        // would later divide by zero in the pagination arithmetic.
        Normalize(new ReportQueryInput()).Query!.WithPaging(1, pageSize).PageSize
            .Should().Be(expected);
    }

    private static ReportQueryResult Normalize(ReportQueryInput input) =>
        ReportQueryNormalizer.Normalize(input, ReportMonitorTypes.All, Now);
}
