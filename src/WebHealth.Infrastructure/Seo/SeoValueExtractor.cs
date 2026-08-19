using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using WebHealth.Application.Seo;
using WebHealth.Domain.Seo;

namespace WebHealth.Infrastructure.Seo;

/// <summary>
/// BR-E01, BR-E10. Parses the body the transport already read and returns extracted values only.
/// <para>
/// The parser is used directly rather than through an <c>IBrowsingContext</c> with a requester, so
/// it has no way to fetch anything a document references: the markup is inert input, not a page
/// being loaded. The class takes no logger and returns no document, which is what keeps the
/// "never retain the HTML" rule structural rather than a convention.
/// </para>
/// </summary>
internal sealed class SeoValueExtractor : ISeoValueExtractor
{
    private static readonly HtmlParser Parser = new();

    static SeoValueExtractor()
    {
        // .NET ships only UTF-*, ASCII and Latin-1 by default, so windows-1252 — still common on
        // exactly the older sites these checks are for — would otherwise fail to resolve and the
        // document would be decoded as UTF-8 into replacement characters. The provider is part of
        // the shared framework on this target, so this costs no dependency.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public SeoExtraction Extract(SeoExtractionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = SeoExtractionRules.NotApplicableReason(new(
            input.TransportSucceeded, input.StatusCode, input.ContentType, input.Body.Length));
        if (reason is not null)
        {
            return SeoExtraction.NotApplicable(reason, input.BodyTruncated);
        }

        try
        {
            return ExtractFromDocument(input);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A document is untrusted input, and this runs on the path that finalises a check.
            // A parse that fails is recorded as a decision; it must never cost the availability
            // result the check was actually for. The exception carries document text, so it is
            // deliberately not logged or stored.
            return SeoExtraction.NotApplicable(
                SeoNotApplicableReasons.ExtractionFailed, input.BodyTruncated);
        }
    }

    private static SeoExtraction ExtractFromDocument(SeoExtractionInput input)
    {
        using var document = Parse(input);

        // Scoped to head: an SVG <title> in the body is a graphic's label, and a meta element
        // outside head is ignored by search engines. Counting either as page metadata would make
        // BR-E02 and BR-E03 judge the wrong thing.
        var head = document.Head;
        var titles = Query(head, "title");
        var descriptions = MetaContents(head, "description");
        var canonicals = Query(head, "link[rel]")
            .Where(element => RelContains(element, "canonical"))
            .Select(element => element.GetAttribute("href"))
            .ToArray();
        var robots = MetaContents(head, "robots");

        // Resolution uses the authored href in full: bounding first would resolve a long canonical
        // from a truncated prefix and record a host that the page never named.
        var authoredCanonical = canonicals.FirstOrDefault(HasText);

        return new(
            SeoApplicabilities.Applicable,
            null,
            input.BodyTruncated,
            SeoExtractionRules.BoundedText(
                titles.Select(element => element.TextContent).FirstOrDefault(HasText),
                SeoValueLimits.Title),
            titles.Count,
            SeoExtractionRules.BoundedText(descriptions.FirstOrDefault(HasText), SeoValueLimits.MetaDescription),
            descriptions.Length,
            SeoExtractionRules.BoundedUrl(authoredCanonical, SeoValueLimits.CanonicalHref),
            canonicals.Length,
            BoundedAbsoluteUrl(SeoExtractionRules.ResolveAbsolute(authoredCanonical, input.FinalUrl)),
            // Every robots meta on the page, combined into one directive list. A page whose
            // first tag says "index" and whose second says "noindex" is noindex, and reading only
            // the first would call it indexable — the directives are cumulative, not first-wins.
            SeoExtractionRules.BoundedText(
                string.Join(", ", robots.Where(HasText)).ToLowerInvariant(), SeoValueLimits.RobotsMeta),
            robots.Length);
    }

    /// <summary>
    /// A resolved URL longer than the stored bound is recorded as no resolved URL rather than as a
    /// truncated one: a cut-off URL names a different resource, and storing it would hand 6.3 a
    /// host comparison against something the page never pointed at. The authored value and its
    /// real length are still recorded, so the canonical is not lost from the history.
    /// </summary>
    private static string? BoundedAbsoluteUrl(string? absoluteUrl) =>
        absoluteUrl is not null && absoluteUrl.Length <= SeoValueLimits.CanonicalHref ? absoluteUrl : null;

    /// <summary>
    /// The declared charset wins when it can be resolved. Otherwise the bytes go to the parser,
    /// which applies the spec's own sniffing (BOM, then a meta charset declaration) and defaults to
    /// UTF-8 when the document declares nothing at all. An unrecognised charset name is treated as
    /// absent: the document is still readable, and refusing to look at it would lose the check.
    /// </summary>
    private static IHtmlDocument Parse(SeoExtractionInput input)
    {
        if (TryGetEncoding(SeoExtractionRules.CharSet(input.ContentType), out var encoding))
        {
            return Parser.ParseDocument(encoding.GetString(input.Body.Span));
        }

        using var stream = new MemoryStream(input.Body.ToArray(), writable: false);
        return Parser.ParseDocument(stream);
    }

    private static bool TryGetEncoding(string? charSet, out Encoding encoding)
    {
        encoding = Encoding.UTF8;
        if (string.IsNullOrWhiteSpace(charSet)) return false;
        try
        {
            encoding = Encoding.GetEncoding(charSet);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IReadOnlyList<IElement> Query(IElement? head, string selector) =>
        head is null ? [] : head.QuerySelectorAll(selector);

    private static string?[] MetaContents(IElement? head, string name) =>
        Query(head, "meta[name]")
            .Where(element => string.Equals(
                element.GetAttribute("name")?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.GetAttribute("content"))
            .ToArray();

    // rel is a space-separated token list, so rel="canonical alternate" is still a canonical link.
    private static bool RelContains(IElement element, string token) =>
        element.GetAttribute("rel")?
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(candidate => string.Equals(candidate, token, StringComparison.OrdinalIgnoreCase)) == true;

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
}
