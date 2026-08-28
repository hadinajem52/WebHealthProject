using WebHealth.Application.Monitoring;

namespace WebHealth.Web.Models;

public sealed record CheckHistoryViewModel(
    CheckHistoryPage Page,
    FilterSummaryViewModel Summary,
    bool CanArchive = false);
public sealed record CheckDetailsViewModel(CheckDetails Check);
