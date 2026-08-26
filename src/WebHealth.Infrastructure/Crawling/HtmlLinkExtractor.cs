using WebHealth.Application.Crawling;
using WebHealth.Application.SiteAnalysis;

namespace WebHealth.Infrastructure.Crawling;

internal sealed class HtmlLinkExtractor(
    IHtmlDocumentDiscoveryExtractor documentExtractor) : IHtmlLinkExtractor
{
    public CrawlDocumentLinks ExtractHrefs(ReadOnlyMemory<byte> body, string? contentType)
    {
        var discovery = documentExtractor.Extract(body, contentType);
        return new(
            discovery.NavigationHrefs,
            discovery.NavigationFullyInspected,
            discovery.BaseHref);
    }
}
