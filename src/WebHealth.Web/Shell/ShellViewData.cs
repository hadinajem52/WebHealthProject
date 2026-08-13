using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace WebHealth.Web.Shell;

/// <summary>
/// Typed access to the view-data entries the shared layout reads. Views set the
/// page heading, optional supporting text and breadcrumb trail; the layout owns
/// how they are rendered.
/// </summary>
public static class ShellViewData
{
    /// <summary>The view-data key holding the page heading.</summary>
    public const string TitleKey = "Title";

    /// <summary>The view-data key holding optional supporting text below the heading.</summary>
    public const string SubtitleKey = "Subtitle";

    /// <summary>The view-data key holding the breadcrumb trail.</summary>
    public const string BreadcrumbsKey = "Breadcrumbs";

    /// <summary>Sets the page heading, which is also used for the document title.</summary>
    public static void SetTitle(this ViewDataDictionary viewData, string title)
    {
        ArgumentNullException.ThrowIfNull(viewData);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        viewData[TitleKey] = title;
    }

    /// <summary>Sets optional supporting text rendered below the page heading.</summary>
    public static void SetSubtitle(this ViewDataDictionary viewData, string subtitle)
    {
        ArgumentNullException.ThrowIfNull(viewData);
        ArgumentException.ThrowIfNullOrWhiteSpace(subtitle);

        viewData[SubtitleKey] = subtitle;
    }

    /// <summary>Sets the breadcrumb trail, ending with the current page.</summary>
    public static void SetBreadcrumbs(this ViewDataDictionary viewData, params BreadcrumbItem[] breadcrumbs)
    {
        ArgumentNullException.ThrowIfNull(viewData);
        ArgumentNullException.ThrowIfNull(breadcrumbs);

        viewData[BreadcrumbsKey] = breadcrumbs;
    }

    /// <summary>Gets the page heading, or <see langword="null" /> when the view did not set one.</summary>
    public static string? GetTitle(this ViewDataDictionary viewData)
    {
        ArgumentNullException.ThrowIfNull(viewData);

        return viewData[TitleKey] as string;
    }

    /// <summary>Gets the supporting text, or <see langword="null" /> when the view did not set any.</summary>
    public static string? GetSubtitle(this ViewDataDictionary viewData)
    {
        ArgumentNullException.ThrowIfNull(viewData);

        return viewData[SubtitleKey] as string;
    }

    /// <summary>Gets the breadcrumb trail, or an empty trail when the view did not set one.</summary>
    public static IReadOnlyList<BreadcrumbItem> GetBreadcrumbs(this ViewDataDictionary viewData)
    {
        ArgumentNullException.ThrowIfNull(viewData);

        return viewData[BreadcrumbsKey] as IReadOnlyList<BreadcrumbItem> ?? [];
    }
}
