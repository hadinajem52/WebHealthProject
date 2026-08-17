using WebHealth.Application.Incidents;
using WebHealth.Application.Registry;

namespace WebHealth.Web.Models;

public sealed record IncidentListViewModel(
    IncidentListPage Page,
    string? Status,
    string? Severity,
    bool UnacknowledgedOnly);

public sealed record IncidentDetailsViewModel(
    IncidentDetails Incident,
    IReadOnlyList<RegistryOwnerOption> OwnerOptions);
