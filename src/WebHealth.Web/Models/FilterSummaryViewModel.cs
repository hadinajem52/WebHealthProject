namespace WebHealth.Web.Models;

public sealed record FilterSummaryItem(string Label, string Value);

public sealed record FilterSummaryWindow(DateTimeOffset Start, DateTimeOffset End);

public sealed record FilterSummaryViewModel(
    DateTimeOffset AsOf,
    IReadOnlyList<FilterSummaryItem> Filters,
    FilterSummaryWindow? Window = null,
    string? Note = null)
{
    public bool IsUnfiltered => Filters.Count == 0;
}
