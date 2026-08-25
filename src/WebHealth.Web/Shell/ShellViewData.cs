using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace WebHealth.Web.Shell;

public static class ShellViewData
{
    public const string TitleKey = "Title";

    public const string BreadcrumbsKey = "Breadcrumbs";

    public const string SubtitleKey = "Subtitle";

    public static void SetTitle(this ViewDataDictionary viewData, string title)
    {
        ArgumentNullException.ThrowIfNull(viewData);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        viewData[TitleKey] = title;
    }

    public static void SetSubtitle(this ViewDataDictionary viewData, string subtitle)
    {
        ArgumentNullException.ThrowIfNull(viewData);
        ArgumentException.ThrowIfNullOrWhiteSpace(subtitle);

        viewData[SubtitleKey] = subtitle;
    }

    public static void SetBreadcrumbs(this ViewDataDictionary viewData, params BreadcrumbItem[] breadcrumbs)
    {
        ArgumentNullException.ThrowIfNull(viewData);
        ArgumentNullException.ThrowIfNull(breadcrumbs);

        viewData[BreadcrumbsKey] = breadcrumbs;
    }

    public static string? GetTitle(this ViewDataDictionary viewData)
    {
        ArgumentNullException.ThrowIfNull(viewData);

        return viewData[TitleKey] as string;
    }

    public static string? GetSubtitle(this ViewDataDictionary viewData)
    {
        ArgumentNullException.ThrowIfNull(viewData);

        return viewData[SubtitleKey] as string;
    }

    public static IReadOnlyList<BreadcrumbItem> GetBreadcrumbs(this ViewDataDictionary viewData)
    {
        ArgumentNullException.ThrowIfNull(viewData);

        return viewData[BreadcrumbsKey] as IReadOnlyList<BreadcrumbItem> ?? [];
    }
}
