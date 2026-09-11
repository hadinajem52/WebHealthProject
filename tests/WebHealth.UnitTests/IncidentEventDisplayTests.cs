using FluentAssertions;
using WebHealth.Application.Incidents;
using WebHealth.Domain.Incidents;
using WebHealth.Web.Models;
using WebHealth.Web.Shell;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class IncidentEventDisplayTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 8, 23, 15, 13, 0, TimeSpan.Zero);

    [Fact]
    public void Name_ReadsAnAuthoredNoteAndAnAutomatedSeverityChangeDifferently()
    {
        var note = "Severity escalated from Warning to Critical.";

        IncidentEventDisplay.Name(Entry(IncidentEventTypes.NoteAdded, note: note))
            .Should().Be("Severity escalated");
        IncidentEventDisplay.Name(Entry(IncidentEventTypes.NoteAdded, note: note, actor: "Administrator"))
            .Should().Be("Note added");
    }

    [Fact]
    public void Tone_FollowsTheStatusAStatusChangeMovedTo()
    {
        IncidentEventDisplay.Tone(Entry(IncidentEventTypes.StatusChanged, toStatus: IncidentStatuses.Resolved))
            .Should().Be(StatusBadges.Success);
        IncidentEventDisplay.Tone(Entry(IncidentEventTypes.StatusChanged, toStatus: IncidentStatuses.Open))
            .Should().Be(StatusBadges.Danger);
        IncidentEventDisplay.Tone(Entry(IncidentEventTypes.Reassigned))
            .Should().Be(StatusBadges.Neutral);
    }

    [Theory]
    [InlineData(0, 0, 30, "after less than a minute")]
    [InlineData(0, 0, 59, "after less than a minute")]
    [InlineData(0, 1, 0, "after 1m")]
    [InlineData(0, 59, 0, "after 59m")]
    [InlineData(0, 60, 0, "after 1h 0m")]
    [InlineData(0, 457, 0, "after 7h 37m")]
    [InlineData(1, 0, 0, "after 1d 0h")]
    [InlineData(3, 120, 0, "after 3d 2h")]
    public void Elapsed_DescribesTheGapAtEachUnitBoundary(
        int days, int minutes, int seconds, string expected)
    {
        IncidentEventDisplay.Elapsed(new TimeSpan(days, 0, minutes, seconds))
            .Should().Be(expected);
    }

    [Fact]
    public void Elapsed_SaysNothingForTheFirstEventOnAPage()
    {
        IncidentEventDisplay.Elapsed(null).Should().BeNull();
    }

    private static IncidentTimelineEntry Entry(
        string eventType,
        string? toStatus = null,
        string? note = null,
        string? actor = null) =>
        new(Guid.NewGuid(),
            1,
            eventType,
            null,
            toStatus,
            null,
            null,
            note,
            actor,
            OccurredAt,
            null);
}
