namespace WebHealth.Web.Models;

public sealed record FilterSummaryItem(string Label, string Value);

/// <summary>
/// The half-open period the figures cover. It carries the two instants rather than a sentence
/// built in the controller: a formatted string cannot be re-rendered in the reader's time zone,
/// so the window would be the one row of this summary still stating UTC while everything around
/// it followed the display setting.
/// </summary>
public sealed record FilterSummaryWindow(DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// BR-R01's disclosure: what the numbers on this screen were filtered to, and the instant they
/// were computed at. Without it a dashboard is unreadable — two people looking at the same page
/// with different filters, or the same page ten minutes apart, cannot tell why their totals
/// differ.
/// </summary>
/// <param name="AsOf">
/// When the data was read. Rendered like every other instant in the interface: UTC in the markup,
/// shown in whichever zone the reader chose, with the other reading on hover.
/// </param>
public sealed record FilterSummaryViewModel(
    DateTimeOffset AsOf,
    IReadOnlyList<FilterSummaryItem> Filters,
    FilterSummaryWindow? Window = null,
    string? Note = null)
{
    /// <summary>
    /// True when no filter narrowed the view. Said out loud rather than shown as a blank row,
    /// so "everything you can see" is never confused with "nothing was selected".
    /// </summary>
    public bool IsUnfiltered => Filters.Count == 0;
}
