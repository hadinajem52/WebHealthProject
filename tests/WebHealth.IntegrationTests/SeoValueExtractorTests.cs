using System.Text;
using FluentAssertions;
using WebHealth.Application.Seo;
using WebHealth.Domain.Seo;
using WebHealth.Infrastructure.Seo;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// BR-E01, BR-E02, BR-E03, BR-E10 against the real parser. No database and no network: the
/// extractor is handed the bytes a check already read.
/// </summary>
public sealed class SeoValueExtractorTests
{
    private const string Marker = "SECRET-BODY-MARKER-8f3a1c";
    private static readonly SeoValueExtractor Extractor = new();

    private static SeoExtraction Extract(
        string html,
        string? contentType = "text/html; charset=utf-8",
        string? finalUrl = "https://example.test/a/b",
        bool truncated = false,
        int? status = 200,
        bool succeeded = true,
        Encoding? encoding = null) =>
        Extractor.Extract(new(
            succeeded, status, contentType, finalUrl, truncated,
            (encoding ?? Encoding.UTF8).GetBytes(html)));

    [Fact]
    public void Extract_ReadsTheFourValuesFromAWellFormedDocument()
    {
        var extraction = Extract("""
            <!doctype html><html><head>
            <title>  Home   page </title>
            <meta name="description" content="A description.">
            <link rel="canonical" href="/canonical">
            <meta name="robots" content="NOINDEX, follow">
            </head><body><p>ignored</p></body></html>
            """);

        extraction.IsApplicable.Should().BeTrue();
        extraction.NotApplicableReason.Should().BeNull();
        extraction.Title.Should().Be(new SeoValue("Home page", 9));
        extraction.MetaDescription.Value.Should().Be("A description.");
        extraction.CanonicalHref.Value.Should().Be("/canonical");
        extraction.CanonicalAbsoluteUrl.Should().Be("https://example.test/canonical");
        extraction.RobotsMeta.Value.Should().Be("noindex, follow", "the directive is compared lowercased");
        extraction.TitleCount.Should().Be(1);
        extraction.MetaDescriptionCount.Should().Be(1);
        extraction.CanonicalCount.Should().Be(1);
        extraction.RobotsMetaCount.Should().Be(1);
    }

    [Fact]
    public void Extract_CountsDuplicatesSoThePolicyLayerCanDistinguishMissingFromDuplicate()
    {
        var extraction = Extract("""
            <html><head>
            <title>First</title><title>Second</title>
            <meta name="description" content="one"><meta name="Description" content="two">
            <link rel="canonical" href="https://a.test/"><link rel="canonical" href="https://b.test/">
            </head><body></body></html>
            """);

        extraction.TitleCount.Should().Be(2);
        extraction.MetaDescriptionCount.Should().Be(2, "the name attribute is matched case-insensitively");
        extraction.CanonicalCount.Should().Be(2);
        extraction.Title.Value.Should().Be("First", "the first non-empty value is the one recorded");
        extraction.CanonicalHref.Value.Should().Be("https://a.test/");
    }

    [Fact]
    public void Extract_RecordsNoValuesAndZeroCountsWhenTheDocumentHasNone()
    {
        var extraction = Extract("<html><head></head><body><p>text</p></body></html>");

        extraction.IsApplicable.Should().BeTrue();
        extraction.Title.Should().Be(SeoValue.None);
        extraction.MetaDescription.Should().Be(SeoValue.None);
        extraction.CanonicalHref.Should().Be(SeoValue.None);
        extraction.CanonicalAbsoluteUrl.Should().BeNull();
        extraction.RobotsMeta.Should().Be(SeoValue.None);
        extraction.TitleCount.Should().Be(0);
    }

    [Fact]
    public void Extract_SkipsAnEmptyTitleButStillCountsIt()
    {
        var extraction = Extract("<html><head><title>   </title><title>Real</title></head></html>");

        extraction.Title.Value.Should().Be("Real");
        extraction.TitleCount.Should().Be(2);
    }

    [Fact]
    public void Extract_TreatsRelAsATokenList()
    {
        var extraction = Extract(
            """<html><head><link rel="Canonical alternate" href="https://example.test/c"></head></html>""");

        extraction.CanonicalCount.Should().Be(1);
        extraction.CanonicalHref.Value.Should().Be("https://example.test/c");
    }

    [Fact]
    public void Extract_IgnoresMarkupInsideCommentsAndScripts()
    {
        var extraction = Extract("""
            <html><head>
            <!-- <title>Commented</title> <meta name="description" content="commented"> -->
            <script>var x = "<title>Scripted</title>";</script>
            <title>Real title</title>
            <meta name=description content=unquoted>
            </head><body></body></html>
            """);

        extraction.Title.Value.Should().Be("Real title");
        extraction.TitleCount.Should().Be(1, "a title inside a comment or a script is not a title element");
        extraction.MetaDescriptionCount.Should().Be(1);
        extraction.MetaDescription.Value.Should().Be("unquoted", "unquoted attribute values are still values");
    }

    /// <summary>
    /// An unclosed title is RCDATA: a browser reads everything after it as title text rather than
    /// as markup, and so does this. Getting this wrong in the other direction — treating the
    /// swallowed meta as a real element — is exactly the failure a pattern-matching extractor
    /// would produce.
    /// </summary>
    [Fact]
    public void Extract_TreatsAnUnclosedTitleAsTextTheWayABrowserWould()
    {
        var extraction = Extract("""
            <html><head>
            <title>Real title
            <meta name="description" content="swallowed">
            </head><body></body></html>
            """);

        extraction.Title.Value.Should().StartWith("Real title");
        extraction.MetaDescriptionCount.Should().Be(0);
        extraction.MetaDescription.Should().Be(SeoValue.None);
    }

    /// <summary>
    /// 0x93/0x94 are windows-1252 smart quotes with no Latin-1 or UTF-8 meaning, so this passes
    /// only if the declared charset is genuinely honoured. The sniffing fallback decodes them as
    /// replacement characters, which is what makes them the right bytes to test with.
    /// </summary>
    [Fact]
    public void Extract_IgnoresHeadLikeElementsFoundInTheBody()
    {
        var extraction = Extract("""
            <html><head><title>Page title</title></head>
            <body>
            <svg><title>Icon label</title></svg>
            <meta name="description" content="body level">
            <link rel="canonical" href="https://wrong.test/">
            </body></html>
            """);

        extraction.Title.Value.Should().Be("Page title");
        extraction.TitleCount.Should().Be(1, "an SVG title is a graphic's label, not the page title");
        extraction.MetaDescriptionCount.Should().Be(0);
        extraction.CanonicalCount.Should().Be(0);
        extraction.CanonicalAbsoluteUrl.Should().BeNull();
    }

    [Fact]
    public void Extract_ResolvesTheCanonicalFromTheFullAuthoredHrefNotATruncatedOne()
    {
        // Longer than the stored bound, so resolving after bounding would name a different path.
        var path = new string('p', SeoValueLimits.CanonicalHref);
        var extraction = Extract($"""<html><head><link rel="canonical" href="/{path}"></head></html>""");

        extraction.CanonicalHref.Value.Should().HaveLength(SeoValueLimits.CanonicalHref);
        extraction.CanonicalHref.Length.Should().Be(path.Length + 1);
        extraction.CanonicalAbsoluteUrl.Should().BeNull(
            "a resolved URL past the stored bound is recorded as absent rather than as a truncated, wrong URL");
    }

    [Fact]
    public void Extract_KeepsTheCanonicalHrefAsAuthored()
    {
        var extraction = Extract(
            """<html><head><link rel="canonical" href="  /a  b  "></head></html>""");

        extraction.CanonicalHref.Value.Should().Be("/a  b", "the authored value is the diagnostic evidence");
    }

    [Fact]
    public void Extract_RecordsAFailedParseAsADecisionRatherThanThrowing()
    {
        // Deeply nested markup is the shape that makes a tree builder give up; whatever the parser
        // does with it, finalization must survive with a recorded reason.
        var extraction = Extract(string.Concat(Enumerable.Repeat("<div>", 5000)));

        extraction.Should().NotBeNull();
        if (!extraction.IsApplicable)
        {
            extraction.NotApplicableReason.Should().Be(SeoNotApplicableReasons.ExtractionFailed);
        }
    }

    [Fact]
    public void Extract_DecodesUsingTheDeclaredCharset()
    {
        var bytes = "<html><head><title>\u201cquoted\u201d</title></head></html>"
            .Select(character => character switch
            {
                '\u201c' => (byte)0x93,
                '\u201d' => (byte)0x94,
                _ => (byte)character
            }).ToArray();

        var extraction = Extractor.Extract(new(
            true, 200, "text/html; charset=windows-1252", "https://example.test/", false, bytes));

        extraction.Title.Value.Should().Be("\u201cquoted\u201d");
        extraction.Title.Value.Should().NotContain("\ufffd", "the declared charset must actually be used");
    }

    [Fact]
    public void Extract_FallsBackToSniffingWhenTheCharsetIsUnrecognised()
    {
        var extraction = Extract(
            """<html><head><meta charset="utf-8"><title>Sniffed</title></head></html>""",
            contentType: "text/html; charset=not-a-real-charset");

        extraction.IsApplicable.Should().BeTrue("an unusable charset name must not lose the check");
        extraction.Title.Value.Should().Be("Sniffed");
    }

    [Fact]
    public void Extract_FlagsATruncatedDocumentWhileStillReadingItsHead()
    {
        var extraction = Extract(
            "<html><head><title>Cut short</title></head><body><p>half a p",
            truncated: true);

        extraction.IsApplicable.Should().BeTrue();
        extraction.DocumentTruncated.Should().BeTrue(
            "an absence-based rule must not fire from a document that was cut short");
        extraction.Title.Value.Should().Be("Cut short");
    }

    [Theory]
    [InlineData("application/pdf", 200, true, SeoNotApplicableReasons.NonHtml)]
    [InlineData(null, 200, true, SeoNotApplicableReasons.NonHtml)]
    [InlineData("text/html", 404, true, SeoNotApplicableReasons.NonSuccessStatus)]
    [InlineData("text/html", 200, false, SeoNotApplicableReasons.TransportFailed)]
    public void Extract_RecordsWhyItDidNotRun(
        string? contentType, int status, bool succeeded, string expectedReason)
    {
        var extraction = Extract(
            $"<html><head><title>{Marker}</title></head></html>",
            contentType: contentType, status: status, succeeded: succeeded);

        extraction.IsApplicable.Should().BeFalse();
        extraction.NotApplicableReason.Should().Be(expectedReason);
        extraction.Title.Should().Be(SeoValue.None);
        extraction.TitleCount.Should().Be(0);
    }

    [Fact]
    public void Extract_RecordsAnEmptyBodyRatherThanParsingIt()
    {
        var extraction = Extractor.Extract(new(true, 200, "text/html", "https://example.test/", false, default));

        extraction.NotApplicableReason.Should().Be(SeoNotApplicableReasons.EmptyBody);
    }

    [Fact]
    public void Extract_BoundsAStoredValueWhileReportingItsRealLength()
    {
        var description = new string('d', SeoValueLimits.MetaDescription + 500);

        var extraction = Extract($"""<html><head><meta name="description" content="{description}"></head></html>""");

        extraction.MetaDescription.Value.Should().HaveLength(SeoValueLimits.MetaDescription);
        extraction.MetaDescription.Length.Should().Be(description.Length);
    }

    /// <summary>
    /// BR-E10, asserted as absence. The document body, its scripts, its comments and its text all
    /// carry a distinctive marker; nothing the extractor returns may contain it, because the only
    /// values it is allowed to keep are the four extracted ones.
    /// </summary>
    [Fact]
    public void Extract_ReturnsNothingThatContainsTheDocument()
    {
        var extraction = Extract($"""
            <html><head>
            <title>Title</title>
            <meta name="description" content="Description.">
            <link rel="canonical" href="https://example.test/canonical">
            <meta name="robots" content="index, follow">
            <meta name="author" content="{Marker}">
            <!-- {Marker} -->
            <script>var secret = "{Marker}";</script>
            </head><body><h1>{Marker}</h1><p>{Marker}</p></body></html>
            """);

        var stored = new[]
        {
            extraction.Title.Value,
            extraction.MetaDescription.Value,
            extraction.CanonicalHref.Value,
            extraction.CanonicalAbsoluteUrl,
            extraction.RobotsMeta.Value,
            extraction.NotApplicableReason,
            extraction.Applicability
        };

        stored.Should().OnlyContain(value => value == null || !value.Contains(Marker, StringComparison.Ordinal),
            "extraction keeps values, never the document");
        extraction.Title.Value.Should().Be("Title", "and the values it does keep are still correct");
    }

    /// <summary>
    /// The contract itself must offer no way to carry markup out of extraction: a future caller
    /// cannot persist or log a document through a member that does not exist.
    /// </summary>
    [Fact]
    public void SeoExtraction_ExposesOnlyExtractedValues() =>
        typeof(SeoExtraction).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(SeoExtraction.Applicability),
                nameof(SeoExtraction.NotApplicableReason),
                nameof(SeoExtraction.DocumentTruncated),
                nameof(SeoExtraction.Title),
                nameof(SeoExtraction.TitleCount),
                nameof(SeoExtraction.MetaDescription),
                nameof(SeoExtraction.MetaDescriptionCount),
                nameof(SeoExtraction.CanonicalHref),
                nameof(SeoExtraction.CanonicalCount),
                nameof(SeoExtraction.CanonicalAbsoluteUrl),
                nameof(SeoExtraction.RobotsMeta),
                nameof(SeoExtraction.RobotsMetaCount),
                nameof(SeoExtraction.IsApplicable));
}
