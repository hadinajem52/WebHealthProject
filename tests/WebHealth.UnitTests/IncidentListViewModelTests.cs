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

    [Fact]
    public void HasFilters_IsFalse_WhenNothingApplied()
    {
        Create().HasFilters.Should().BeFalse();
    }

    [Fact]
    public void HasFilters_IsFalse_WhenStatusAndSeverityAreWhitespace()
    {
        Create(status: "   ", severity: " ").HasFilters.Should().BeFalse();
        Create(status: "\t", severity: "\n").HasFilters.Should().BeFalse();
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("Closed")]
    [InlineData("InProgress")]
    public void HasFilters_IsTrue_WhenStatusSet(string status)
    {
        Create(status: status).HasFilters.Should().BeTrue();
    }

    [Theory]
    [InlineData("Critical")]
    [InlineData("Warning")]
    public void HasFilters_IsTrue_WhenSeveritySet(string severity)
    {
        Create(severity: severity).HasFilters.Should().BeTrue();
    }

    [Fact]
    public void HasFilters_IsTrue_WhenUnacknowledgedOnly()
    {
        Create(unacknowledgedOnly: true).HasFilters.Should().BeTrue();
    }

    [Fact]
    public void HasFilters_IsTrue_WhenAnyFilterSet()
    {
        Create(status: "Open", severity: "Critical", unacknowledgedOnly: true).HasFilters.Should().BeTrue();
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
