using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace WebHealth.Web.Shell;

/// <summary>
/// Renders an instant as a <c>&lt;time&gt;</c> element carrying both the UTC text the server
/// produced and the machine-readable instant behind it.
/// <para>
/// The stored value is UTC and stays UTC — the database, every comparison and every constraint
/// depend on it. Which zone a reader sees is a presentation choice made in the browser, so the
/// element ships the round-trip instant in <c>datetime</c> and shell.js reformats it in place.
/// Reparsing the rendered text instead would mean parsing a display string back into a moment,
/// which is the sort of guess that quietly breaks the first time a format changes.
/// </para>
/// <para>
/// Without script the UTC text stands on its own, so the page is never blank or ambiguous.
/// </para>
/// </summary>
public static class TimestampHtml
{
    public const string UtcMinuteFormat = "yyyy-MM-dd HH:mm";
    public const string UtcSecondFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>An instant shown to the minute.</summary>
    public static IHtmlContent Timestamp(this IHtmlHelper html, DateTimeOffset value) =>
        Render(value, UtcMinuteFormat, withSeconds: false);

    /// <summary>An instant shown to the second, for timelines and audit records.</summary>
    public static IHtmlContent TimestampWithSeconds(this IHtmlHelper html, DateTimeOffset value) =>
        Render(value, UtcSecondFormat, withSeconds: true);

    /// <summary>An optional instant to the minute, with the wording to use when it is absent.</summary>
    public static IHtmlContent Timestamp(this IHtmlHelper html, DateTimeOffset? value, string fallback = "—") =>
        value is { } present ? Render(present, UtcMinuteFormat, withSeconds: false) : new HtmlString(fallback);

    /// <summary>An optional instant to the second, with the wording to use when it is absent.</summary>
    public static IHtmlContent TimestampWithSeconds(
        this IHtmlHelper html,
        DateTimeOffset? value,
        string fallback = "—") =>
        value is { } present ? Render(present, UtcSecondFormat, withSeconds: true) : new HtmlString(fallback);

    private static IHtmlContent Render(DateTimeOffset value, string format, bool withSeconds)
    {
        var utc = value.ToUniversalTime();
        var builder = new TagBuilder("time");

        // Round-trip ("o") is unambiguous and parses in every browser without a shim.
        builder.Attributes["datetime"] = utc.ToString("o", CultureInfo.InvariantCulture);
        builder.Attributes["data-utc-time"] = withSeconds ? "seconds" : "minutes";
        builder.InnerHtml.Append($"{utc.ToString(format, CultureInfo.InvariantCulture)} UTC");
        return builder;
    }
}
