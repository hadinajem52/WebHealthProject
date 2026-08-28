using WebHealth.Web.Shell;

namespace WebHealth.Web.Models;

public sealed record StatusBadgeViewModel(
    string Status,
    string Label,
    string? Detail = null,
    bool Pending = false,
    string? AnimationKey = null,
    string? LiveKey = null)
{
    public string Icon => StatusBadges.Icon(Status);
}
