namespace WebHealth.Web.Models;

public sealed record FilterSummaryItem(string Label, string Value);

/// <summary>
/// BR-R01's disclosure: what the numbers on this screen were filtered to, and the instant they
/// were computed at. Without it a dashboard is unreadable — two people looking at the same page
/// with different filters, or the same page ten minutes apart, cannot tell why their totals
/// differ.
/// </summary>
/// <param name="AsOf">
/// When the data was read. Rendered in UTC, because every stored instant in this system is UTC
/// and a local rendering would invite a reader to compare it against a differently-zoned figure.
/// </param>
public sealed record FilterSummaryViewModel(
    DateTimeOffset AsOf,
    IReadOnlyList<FilterSummaryItem> Filters,
    string? Window = null,
    string? Note = null)
{
    /// <summary>
    /// True when no filter narrowed the view. Said out loud rather than shown as a blank row,
    /// so "everything you can see" is never confused with "nothing was selected".
    /// </summary>
    public bool IsUnfiltered => Filters.Count == 0;
}
