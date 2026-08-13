namespace WebHealth.Web.Models;

/// <summary>
/// Content for the shared empty-state component used when a screen has no data
/// or a filter returns no results.
/// </summary>
/// <param name="Title">Short statement of what is missing.</param>
/// <param name="Description">Safe explanation of why the area is empty.</param>
/// <param name="IconKey">Key resolved by the shared icon partial.</param>
/// <param name="ActionText">Label of the safe next action, when one exists.</param>
/// <param name="ActionUrl">Destination of the safe next action, when one exists.</param>
public sealed record EmptyStateViewModel(
    string Title,
    string Description,
    string IconKey = "empty",
    string? ActionText = null,
    string? ActionUrl = null);
