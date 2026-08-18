using WebHealth.Application.Monitoring;

namespace WebHealth.Web.Models;

public sealed record CheckHistoryViewModel(CheckHistoryPage Page, FilterSummaryViewModel Summary);
public sealed record CheckDetailsViewModel(CheckDetails Check);
