using System.Text;
using FluentAssertions;
using WebHealth.Application.SiteAnalysis;
using WebHealth.Infrastructure.Crawling;
using WebHealth.Infrastructure.SiteAnalysis;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class HtmlDocumentDiscoveryExtractorTests
{
    [Fact]
    public void Extract_FindsNavigationBaseAndImageSources()
    {
        var html = "<!doctype html><html><head><base href=\"/assets/\"></head><body>"
            + "<a href=\"/page\">page</a><area href=\"/map\">"
            + "<img src=\"/hero.png\" srcset=\"/hero-small.png 1x, /hero-large.png 2x\">"
            + "<picture><source src=\"/unsupported.png\" srcset=\"/wide.png 1200w\">"
            + "<img src=\"/fallback.png\"></picture>"
            + "<source srcset=\"/not-a-picture.png 1x\">"
            + "</body></html>";

        var result = Extract(html);

        result.NavigationHrefs.Should().Equal("/page", "/map");
        result.BaseHref.Should().Be("/assets/");
        result.NavigationFullyInspected.Should().BeTrue();
        result.ImageReferencesFullyInspected.Should().BeTrue();
        result.Images.Should().Equal(
            new HtmlImageReference("/hero.png", HtmlImageAttributeKinds.ImageSource, null),
            new HtmlImageReference("/hero-small.png", HtmlImageAttributeKinds.ImageSourceSet, "1x"),
            new HtmlImageReference("/hero-large.png", HtmlImageAttributeKinds.ImageSourceSet, "2x"),
            new HtmlImageReference("/wide.png", HtmlImageAttributeKinds.PictureSourceSet, "1200w"),
            new HtmlImageReference("/fallback.png", HtmlImageAttributeKinds.ImageSource, null));
    }

    [Fact]
    public void Extract_TracksImageCoverageIndependentlyFromNavigationCoverage()
    {
        var images = string.Concat(Enumerable.Range(
                0,
                HtmlDocumentDiscoveryLimits.MaxImageReferences + 1)
            .Select(index => $"<img src=\"/image-{index}.png\">"));

        var result = Extract($"<html><body><a href=\"/page\">page</a>{images}</body></html>");

        result.NavigationHrefs.Should().Equal("/page");
        result.NavigationFullyInspected.Should().BeTrue();
        result.Images.Should().HaveCount(HtmlDocumentDiscoveryLimits.MaxImageReferences);
        result.ImageReferencesFullyInspected.Should().BeFalse();
    }

    [Fact]
    public void Extract_ParsesADataUrlCommaAsPartOfOneSourceSetCandidate()
    {
        var result = Extract(
            "<html><body><img srcset=\"data:image/png;base64,AAAA 1x, /image.png 2x\"></body></html>");

        result.Images.Should().Equal(
            new HtmlImageReference(
                "data:image/png;base64,AAAA",
                HtmlImageAttributeKinds.ImageSourceSet,
                "1x"),
            new HtmlImageReference(
                "/image.png",
                HtmlImageAttributeKinds.ImageSourceSet,
                "2x"));
    }

    [Fact]
    public void LinkAdapter_ExposesOnlyNavigationDiscovery()
    {
        var extractor = new HtmlDocumentDiscoveryExtractor();
        var adapter = new HtmlLinkExtractor(extractor);
        var body = Encoding.UTF8.GetBytes(
            "<html><head><base href=\"/base/\"></head><body>"
            + "<a href=\"page\">page</a><img src=\"image.png\"></body></html>");

        var result = adapter.ExtractHrefs(body, "text/html; charset=utf-8");

        result.Hrefs.Should().Equal("page");
        result.BaseHref.Should().Be("/base/");
        result.FullyInspected.Should().BeTrue();
    }

    private static HtmlDocumentDiscovery Extract(string html) =>
        new HtmlDocumentDiscoveryExtractor().Extract(
            Encoding.UTF8.GetBytes(html),
            "text/html; charset=utf-8");
}
