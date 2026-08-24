using WebHealth.Application.Incidents;
using WebHealth.Application.Registry;

namespace WebHealth.Web.Models;

public sealed record IncidentListViewModel(
    IncidentListPage Page,
    string? Status,
    string? Severity,
    bool UnacknowledgedOnly,
    FilterSummaryViewModel Summary,
    bool CanArchive = false)
{
    /// <summary>
    /// How many incidents the archive sweep would move. It is deliberately the unfiltered total
    /// rather than what this page shows, because the sweep is unfiltered too.
    /// </summary>
    public int ResolvedCount => Page.ArchivableCount;

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

/// <summary>
/// The archive view. It carries no filters of its own: the archive is a place incidents are put,
/// not a slice of the working list, and a second set of filters over it would only invite the
/// question of which list a reader is looking at.
/// </summary>
public sealed record IncidentArchiveViewModel(IncidentListPage Page, bool CanRestore);

public sealed record IncidentDetailsViewModel(
    IncidentDetails Incident,
    IReadOnlyList<RegistryOwnerOption> OwnerOptions);
