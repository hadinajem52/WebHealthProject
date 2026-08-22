using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace WebHealth.Web.Shell;

/// <summary>
/// Renders the small markdown subset Lighthouse puts in audit titles and descriptions.
///
/// <para>
/// Google returns strings such as <c>Descriptive link text helps search engines understand your
/// content. [Learn how to make links more accessible](https://developer.chrome.com/…)</c> and
/// <c>Document has a valid `rel=canonical`</c>. Rendered as plain text those read as syntax
/// errors: a reader sees literal brackets, a bare URL they cannot click, and backticks around
/// identifiers. This turns the two constructs Lighthouse actually uses — links and code spans —
/// into real markup, and leaves everything else alone.
/// </para>
///
/// <para>
/// The input is third-party text from an outbound HTTP call, so it is treated as untrusted. The
/// method never passes any part of it through unencoded: every literal run, every link label and
/// every code span is HTML-encoded as it is appended, and a URL only becomes an
/// <c>href</c> after it parses as an absolute <c>http</c> or <c>https</c> URI. Anything else —
/// <c>javascript:</c>, <c>data:</c>, a relative path, a malformed URI — is written out as plain
/// text, so a hostile or broken value degrades into something unclickable rather than into
/// markup. Encoding the whole string first and pattern-matching afterwards would be the easy
/// version and the wrong one: the patterns would then have to match against entities.
/// </para>
/// </summary>
public static class LighthouseTextHtml
{
    /// <summary>
    /// A markdown link. The label stops at the first <c>]</c> and the target at the first
    /// whitespace or <c>)</c>, so an unterminated construct simply fails to match and stays text.
    /// </summary>
    private static readonly Regex LinkPattern = new(
        @"\[(?<label>[^\]]*)\]\((?<url>[^\s()]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    /// <summary>A backtick code span, non-greedy so adjacent spans do not merge into one.</summary>
    private static readonly Regex CodePattern = new(
        @"`(?<code>[^`]+)`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    /// <summary>
    /// Lighthouse prose with its links and code spans rendered. Null or blank input yields
    /// <see cref="HtmlString.Empty"/> so callers do not each need a null check.
    /// </summary>
    public static IHtmlContent LighthouseText(this IHtmlHelper html, string? value) =>
        Render(value);

    /// <summary>
    /// The same rendering, with the wording to use when the value is absent. The fallback is
    /// encoded too — it is a caller-supplied string, not markup.
    /// </summary>
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

            // Whichever construct starts first wins, so a code span inside a link label and a
            // link inside a sentence are both handled by the same single pass.
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
            // A pathological string is not a reason to fail the page. Giving up on the remaining
            // markup leaves the text readable, which is the whole point of rendering it.
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
        // The label is the reader-facing half; when Lighthouse omits it, the URL is all there is
        // to show, so it becomes the text as well as the target.
        var text = string.IsNullOrWhiteSpace(label) ? url : label;

        if (!IsSafeHttpUrl(url, out var safe))
        {
            // Not a URL this application will link to. Both halves are still worth showing, so
            // the reader can see what was returned rather than a hole in the sentence.
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

        // A label may itself contain a code span -- "Learn more about the `alt` attribute".
        builder.Append(RenderInline(text));
        builder.Append("</a>");
    }

    /// <summary>Code spans only, for text already known to be a link label.</summary>
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

    /// <summary>
    /// Whether this is a URL worth turning into an anchor. Only absolute http and https pass:
    /// a scheme-relative or relative target would resolve against this application's own origin,
    /// and <c>javascript:</c> and <c>data:</c> are exactly what an anchor must never carry.
    /// </summary>
    private static bool IsSafeHttpUrl(string candidate, out string absolute)
    {
        absolute = string.Empty;

        // Lighthouse descriptions end sentences right after the closing parenthesis, and some
        // targets arrive with trailing punctuation attached.
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
