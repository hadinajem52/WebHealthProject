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
    public int ResolvedCount => Page.ArchivableCount;

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Status)
        || !string.IsNullOrWhiteSpace(Severity)
        || UnacknowledgedOnly;

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

public sealed record IncidentArchiveViewModel(IncidentListPage Page, bool CanRestore);

public sealed record IncidentDetailsViewModel(
    IncidentDetails Incident,
    IReadOnlyList<RegistryOwnerOption> OwnerOptions);
