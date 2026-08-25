using FluentAssertions;
using WebHealth.Application.Incidents;
using WebHealth.Web.Models;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class IncidentListViewModelTests
{
    private static IncidentListViewModel Create(
        string? status = null,
        string? severity = null,
        bool unacknowledgedOnly = false) =>
        new(
            new IncidentListPage([], 0, 1, 20),
            status,
            severity,
            unacknowledgedOnly,
            IncidentListViewModel.Describe(DateTimeOffset.UtcNow, status, severity, unacknowledgedOnly));

    [Theory]
    [InlineData(null, null, false, false)]
    [InlineData("   ", "\t", false, false)]
    [InlineData("Open", null, false, true)]
    [InlineData(null, "Critical", false, true)]
    [InlineData(null, null, true, true)]
    public void HasFilters_ReportsWhetherAnyEffectiveFilterIsApplied(
        string? status,
        string? severity,
        bool unacknowledgedOnly,
        bool expected)
    {
        Create(status, severity, unacknowledgedOnly).HasFilters.Should().Be(expected);
    }

    [Fact]
    public void Describe_BuildsSummaryMatchingHasFilters()
    {
        var filtered = Create(status: "Open", severity: "Critical");
        filtered.Summary.Filters.Should().HaveCount(2);
        filtered.HasFilters.Should().BeTrue();

        var unfiltered = Create();
        unfiltered.Summary.Filters.Should().BeEmpty();
        unfiltered.HasFilters.Should().BeFalse();
    }
}
