using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Parser;
using WebHealth.Application.SiteAnalysis;
using WebHealth.Domain.Seo;

namespace WebHealth.Infrastructure.SiteAnalysis;

internal sealed class HtmlDocumentDiscoveryExtractor : IHtmlDocumentDiscoveryExtractor
{
    private static readonly HtmlParser Parser = new();

    static HtmlDocumentDiscoveryExtractor() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public HtmlDocumentDiscovery Extract(ReadOnlyMemory<byte> body, string? contentType)
    {
        if (contentType is null) return HtmlDocumentDiscovery.NotInspected;
        if (!SeoExtractionRules.IsHtml(contentType) || body.Length == 0)
        {
            return HtmlDocumentDiscovery.Nothing;
        }

        try
        {
            using var document = Parse(body, contentType);
            var navigation = ExtractNavigation(document);
            var images = ExtractImages(document);
            return new(
                navigation.Hrefs,
                images.References,
                document.QuerySelector("base[href]")?.GetAttribute("href"),
                navigation.FullyInspected,
                images.FullyInspected);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return HtmlDocumentDiscovery.NotInspected;
        }
    }

    private static NavigationDiscovery ExtractNavigation(IDocument document)
    {
        var hrefs = document.QuerySelectorAll("a[href], area[href]")
            .Select(element => element.GetAttribute("href"))
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .Take(HtmlDocumentDiscoveryLimits.MaxNavigationHrefs + 1)
            .ToArray();
        return hrefs.Length > HtmlDocumentDiscoveryLimits.MaxNavigationHrefs
            ? new(hrefs[..HtmlDocumentDiscoveryLimits.MaxNavigationHrefs]!, false)
            : new(hrefs!, true);
    }

    private static ImageDiscovery ExtractImages(IDocument document)
    {
        var references = new List<HtmlImageReference>();
        var fullyInspected = true;
        foreach (var element in document.QuerySelectorAll("img, picture source"))
        {
            var isImage = string.Equals(element.LocalName, "img", StringComparison.OrdinalIgnoreCase);
            if (isImage)
            {
                fullyInspected &= AddSource(
                    references,
                    element.GetAttribute("src"),
                    HtmlImageAttributeKinds.ImageSource);
            }
            fullyInspected &= AddSourceSet(
                references,
                element.GetAttribute("srcset"),
                isImage ? HtmlImageAttributeKinds.ImageSourceSet : HtmlImageAttributeKinds.PictureSourceSet);
        }

        return new(references, fullyInspected);
    }

    private static bool AddSource(
        List<HtmlImageReference> references,
        string? rawUrl,
        string attributeKind)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return true;
        return Add(references, new(rawUrl, attributeKind, null));
    }

    private static bool AddSourceSet(
        List<HtmlImageReference> references,
        string? sourceSet,
        string attributeKind)
    {
        if (string.IsNullOrWhiteSpace(sourceSet)) return true;
        try
        {
            var fullyInspected = true;
            foreach (var candidate in SourceSet.Parse(sourceSet))
            {
                if (string.IsNullOrWhiteSpace(candidate.Url)) continue;
                fullyInspected &= Add(references, new(
                    candidate.Url,
                    attributeKind,
                    string.IsNullOrWhiteSpace(candidate.Descriptor) ? null : candidate.Descriptor));
            }

            return fullyInspected;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    private static bool Add(
        List<HtmlImageReference> references,
        HtmlImageReference reference)
    {
        if (references.Count >= HtmlDocumentDiscoveryLimits.MaxImageReferences) return false;
        references.Add(reference);
        return true;
    }

    private static AngleSharp.Html.Dom.IHtmlDocument Parse(
        ReadOnlyMemory<byte> body,
        string? contentType)
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

    private sealed record NavigationDiscovery(
        IReadOnlyList<string> Hrefs,
        bool FullyInspected);

    private sealed record ImageDiscovery(
        IReadOnlyList<HtmlImageReference> References,
        bool FullyInspected);
}
