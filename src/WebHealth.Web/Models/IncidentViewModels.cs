using WebHealth.Application.Incidents;
using WebHealth.Application.Registry;

namespace WebHealth.Web.Models;

public sealed record IncidentListViewModel(
    IncidentListPage Page,
    string? Status,
    string? Severity,
    bool UnacknowledgedOnly,
    FilterSummaryViewModel Summary)
{
    /// <summary>
    /// Whether anything narrowed this list. A Clear control offered against an unfiltered list is
    /// an action with nothing to do, and it reads as though a filter is applied when none is.
    /// </summary>
    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Status)
        || !string.IsNullOrWhiteSpace(Severity)
        || UnacknowledgedOnly;

    /// <summary>BR-R01: what this list was narrowed to, named rather than implied.</summary>
    public static FilterSummaryViewModel Describe(
        DateTimeOffset asOf,
        string? status,
        string? severity,
        bool unacknowledgedOnly)
    {
        var filters = new List<FilterSummaryItem>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            filters.Add(new("Status", status));
        }

        if (!string.IsNullOrWhiteSpace(severity))
        {
            filters.Add(new("Severity", severity));
        }

        if (unacknowledgedOnly)
        {
            filters.Add(new("Acknowledgement", "Unacknowledged only"));
        }

        return new(asOf, filters);
    }
}

public sealed record IncidentDetailsViewModel(
    IncidentDetails Incident,
    IReadOnlyList<RegistryOwnerOption> OwnerOptions);
