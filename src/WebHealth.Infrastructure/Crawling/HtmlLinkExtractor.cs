using System.Text;
using AngleSharp.Html.Parser;
using WebHealth.Application.Crawling;
using WebHealth.Domain.Seo;

namespace WebHealth.Infrastructure.Crawling;

internal sealed class HtmlLinkExtractor : IHtmlLinkExtractor
{
    private static readonly HtmlParser Parser = new();

    private const int MaxHrefsPerPage = 5000;

    static HtmlLinkExtractor() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public CrawlDocumentLinks ExtractHrefs(ReadOnlyMemory<byte> body, string? contentType)
    {
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
            var baseHref = document.QuerySelector("base[href]")?.GetAttribute("href");

            return hrefs.Length > MaxHrefsPerPage
                ? new(hrefs[..MaxHrefsPerPage]!, FullyInspected: false, baseHref)
                : new(hrefs!, FullyInspected: true, baseHref);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
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
