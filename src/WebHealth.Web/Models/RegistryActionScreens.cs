using WebHealth.Web.Shell;

namespace WebHealth.Web.Models;

public static class RegistryActionScreens
{
    public const string ViewName = "RegistryActionScreen";

    public static IReadOnlyList<TItem> Merge<TItem>(
        IReadOnlyList<TItem> active,
        IReadOnlyList<TItem> archived,
        Func<TItem, string> orderBy) =>
        active.Concat(archived).OrderBy(orderBy, StringComparer.OrdinalIgnoreCase).ToArray();

    public static string Tone(bool isDeleted, bool isLive) => isDeleted
        ? StatusBadges.Danger
        : isLive ? StatusBadges.Success : StatusBadges.Warning;
}
