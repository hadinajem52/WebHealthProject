using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace WebHealth.Web.Shell;

public static class LighthouseTextHtml
{
    private static readonly Regex LinkPattern = new(
        @"\[(?<label>[^\]]*)\]\((?<url>[^\s()]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex CodePattern = new(
        @"`(?<code>[^`]+)`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    public static IHtmlContent LighthouseText(this IHtmlHelper html, string? value) =>
        Render(value);

    public static IHtmlContent LighthouseText(this IHtmlHelper html, string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? new HtmlString(HtmlEncode(fallback)) : Render(value);

    private static IHtmlContent Render(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return HtmlString.Empty;
        }

        var builder = new StringBuilder(value.Length + 64);
        var cursor = 0;

        while (cursor < value.Length)
        {
            var link = NextMatch(LinkPattern, value, cursor);
            var code = NextMatch(CodePattern, value, cursor);

            var next = Earliest(link, code);
            if (next is null)
            {
                AppendEncoded(builder, value.AsSpan(cursor));
                break;
            }

            AppendEncoded(builder, value.AsSpan(cursor, next.Index - cursor));

            if (next == link)
            {
                AppendLink(builder, next.Groups["label"].Value, next.Groups["url"].Value);
            }
            else
            {
                builder.Append("<code>")
                    .Append(HtmlEncode(next.Groups["code"].Value))
                    .Append("</code>");
            }

            cursor = next.Index + next.Length;
        }

        return new HtmlString(builder.ToString());
    }

    private static Match? NextMatch(Regex pattern, string value, int start)
    {
        try
        {
            var match = pattern.Match(value, start);
            return match.Success ? match : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static Match? Earliest(Match? first, Match? second)
    {
        if (first is null)
        {
            return second;
        }

        return second is null || first.Index <= second.Index ? first : second;
    }

    private static void AppendLink(StringBuilder builder, string label, string url)
    {
        var text = string.IsNullOrWhiteSpace(label) ? url : label;

        if (!IsSafeHttpUrl(url, out var safe))
        {
            AppendEncoded(builder, text.AsSpan());
            if (!string.Equals(text, url, StringComparison.Ordinal))
            {
                builder.Append(" (");
                AppendEncoded(builder, url.AsSpan());
                builder.Append(')');
            }

            return;
        }

        builder.Append("<a href=\"")
            .Append(HtmlEncode(safe))
            .Append("\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">");

        builder.Append(RenderInline(text));
        builder.Append("</a>");
    }

    private static string RenderInline(string value)
    {
        var builder = new StringBuilder(value.Length);
        var cursor = 0;

        while (cursor < value.Length)
        {
            var code = NextMatch(CodePattern, value, cursor);
            if (code is null)
            {
                AppendEncoded(builder, value.AsSpan(cursor));
                break;
            }

            AppendEncoded(builder, value.AsSpan(cursor, code.Index - cursor));
            builder.Append("<code>")
                .Append(HtmlEncode(code.Groups["code"].Value))
                .Append("</code>");
            cursor = code.Index + code.Length;
        }

        return builder.ToString();
    }

    private static bool IsSafeHttpUrl(string candidate, out string absolute)
    {
        absolute = string.Empty;

        var trimmed = candidate.TrimEnd('.', ',', ';');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        absolute = uri.AbsoluteUri;
        return true;
    }

    private static void AppendEncoded(StringBuilder builder, ReadOnlySpan<char> value)
    {
        if (!value.IsEmpty)
        {
            builder.Append(HtmlEncode(value.ToString()));
        }
    }

    private static string HtmlEncode(string value) =>
        HtmlEncoder.Encode(value);

    private static System.Text.Encodings.Web.HtmlEncoder HtmlEncoder =>
        System.Text.Encodings.Web.HtmlEncoder.Default;
}
