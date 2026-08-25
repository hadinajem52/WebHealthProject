using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace WebHealth.Web.Shell;

public static class TimestampHtml
{
    public const string UtcMinuteFormat = "yyyy-MM-dd HH:mm";
    public const string UtcSecondFormat = "yyyy-MM-dd HH:mm:ss";

    public static IHtmlContent Timestamp(this IHtmlHelper html, DateTimeOffset value) =>
        Render(value, UtcMinuteFormat, withSeconds: false);

    public static IHtmlContent TimestampWithSeconds(this IHtmlHelper html, DateTimeOffset value) =>
        Render(value, UtcSecondFormat, withSeconds: true);

    public static IHtmlContent Timestamp(this IHtmlHelper html, DateTimeOffset? value, string fallback = "—") =>
        value is { } present ? Render(present, UtcMinuteFormat, withSeconds: false) : new HtmlString(fallback);

    public static IHtmlContent TimestampWithSeconds(
        this IHtmlHelper html,
        DateTimeOffset? value,
        string fallback = "—") =>
        value is { } present ? Render(present, UtcSecondFormat, withSeconds: true) : new HtmlString(fallback);

    private static IHtmlContent Render(DateTimeOffset value, string format, bool withSeconds)
    {
        var utc = value.ToUniversalTime();
        var builder = new TagBuilder("time");

        builder.Attributes["datetime"] = utc.ToString("o", CultureInfo.InvariantCulture);
        builder.Attributes["data-utc-time"] = withSeconds ? "seconds" : "minutes";
        builder.InnerHtml.Append($"{utc.ToString(format, CultureInfo.InvariantCulture)} UTC");
        return builder;
    }
}
