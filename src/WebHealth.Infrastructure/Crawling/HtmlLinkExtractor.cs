using System.Text;
using AngleSharp.Html.Parser;
using WebHealth.Application.Crawling;
using WebHealth.Domain.Seo;

namespace WebHealth.Infrastructure.Crawling;

/// <summary>
/// BR-E10 and BR-L01. Reads <c>href</c> values out of a document and returns nothing else.
/// <para>
/// The parser is used directly, exactly as <c>SeoValueExtractor</c> uses it: no browsing context
/// and no requester, so the markup is inert input rather than a page being loaded and cannot cause
/// a fetch of anything it references. The class takes no logger and returns no document, which is
/// what keeps "never retain the HTML" structural rather than a convention.
/// </para>
/// </summary>
internal sealed class HtmlLinkExtractor : IHtmlLinkExtractor
{
    private static readonly HtmlParser Parser = new();

    /// <summary>
    /// A page with more anchors than this is a generated index. The frontier's caps would bound the
    /// crawl anyway; this bounds the parse result itself so one hostile document cannot make the
    /// worker allocate without limit.
    /// </summary>
    private const int MaxHrefsPerPage = 5000;

    static HtmlLinkExtractor() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public CrawlDocumentLinks ExtractHrefs(ReadOnlyMemory<byte> body, string? contentType)
    {
        // A response with no declared type is the one case that cannot be judged: it may be markup
        // full of links or a binary with none, and either way its links were not enumerated.
        if (contentType is null) return CrawlDocumentLinks.NotInspected;
        if (!SeoExtractionRules.IsHtml(contentType) || body.Length == 0)
        {
            return CrawlDocumentLinks.Nothing;
        }

        try
        {
            using var document = Parse(body, contentType);
            var hrefs = document.QuerySelectorAll("a[href], area[href]")
                .Select(element => element.GetAttribute("href"))
                .Where(href => !string.IsNullOrWhiteSpace(href))
                .Take(MaxHrefsPerPage + 1)
                .ToArray();

            // One over the cap: the extra element is how a document that has more links than this
            // reads is told apart from one that has exactly as many.
            return hrefs.Length > MaxHrefsPerPage
                ? new(hrefs[..MaxHrefsPerPage]!, FullyInspected: false)
                : new(hrefs!, FullyInspected: true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A document is untrusted input. A parse that fails costs this page's links, never the
            // run. The exception carries document text, so it is deliberately not logged.
            return CrawlDocumentLinks.NotInspected;
        }
    }

    private static AngleSharp.Html.Dom.IHtmlDocument Parse(ReadOnlyMemory<byte> body, string? contentType)
    {
        if (TryGetEncoding(SeoExtractionRules.CharSet(contentType), out var encoding))
        {
            return Parser.ParseDocument(encoding.GetString(body.Span));
        }

        using var stream = new MemoryStream(body.ToArray(), writable: false);
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
}
