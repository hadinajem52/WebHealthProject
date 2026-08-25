namespace WebHealth.Web.Models;

public sealed record EmptyStateViewModel(
    string Title,
    string Description,
    string IconKey = "empty",
    string? ActionText = null,
    string? ActionUrl = null);
