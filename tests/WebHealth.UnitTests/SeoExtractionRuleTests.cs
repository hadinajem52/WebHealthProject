using FluentAssertions;
using WebHealth.Domain.Seo;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// BR-E01 applicability and the BR-E10 bounding rules, as written down in
/// docs/phase-6/SEO_Value_Extraction.md.
/// </summary>
public sealed class SeoExtractionRuleTests
{
    private static SeoApplicabilityInput Html(
        bool succeeded = true, int? status = 200, string? contentType = "text/html", long length = 64) =>
        new(succeeded, status, contentType, length);

    [Fact]
    public void NotApplicableReason_IsNullForASuccessfulHtmlResponse() =>
        SeoExtractionRules.NotApplicableReason(Html()).Should().BeNull();

    [Fact]
    public void NotApplicableReason_RecordsATransportFailure() =>
        SeoExtractionRules.NotApplicableReason(Html(succeeded: false))
            .Should().Be(SeoNotApplicableReasons.TransportFailed);

    [Theory]
    [InlineData(199)]
    [InlineData(301)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(null)]
    public void NotApplicableReason_RejectsAnythingButSuccess(int? status) =>
        SeoExtractionRules.NotApplicableReason(Html(status: status))
            .Should().Be(SeoNotApplicableReasons.NonSuccessStatus);

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/json")]
    [InlineData("image/png")]
    [InlineData("text/plain")]
    [InlineData(null)]
    [InlineData("")]
    public void NotApplicableReason_RejectsNonHtmlIncludingAnUnlabelledBody(string? contentType) =>
        SeoExtractionRules.NotApplicableReason(Html(contentType: contentType))
            .Should().Be(SeoNotApplicableReasons.NonHtml,
                "an unlabelled body must not be guessed at, which is the binary content BR-E01 forbids");

    [Fact]
    public void NotApplicableReason_RejectsAnEmptyBody() =>
        SeoExtractionRules.NotApplicableReason(Html(length: 0))
            .Should().Be(SeoNotApplicableReasons.EmptyBody);

    [Theory]
    [InlineData("text/html", true)]
    [InlineData("TEXT/HTML", true)]
    [InlineData("text/html; charset=utf-8", true)]
    [InlineData("  text/html ;charset=UTF-8", true)]
    [InlineData("application/xhtml+xml", true)]
    [InlineData("text/htmlish", false)]
    [InlineData("application/xml", false)]
    public void IsHtml_ComparesTheMediaTypeWithoutItsParameters(string contentType, bool expected) =>
        SeoExtractionRules.IsHtml(contentType).Should().Be(expected);

    [Theory]
    [InlineData("text/html; charset=utf-8", "utf-8")]
    [InlineData("text/html;charset=\"ISO-8859-1\"", "ISO-8859-1")]
    [InlineData("text/html; boundary=x; CharSet = windows-1252", "windows-1252")]
    [InlineData("text/html", null)]
    [InlineData("text/html; charset=", null)]
    [InlineData(null, null)]
    public void CharSet_ReadsTheDeclaredEncodingWhenThereIsOne(string? contentType, string? expected) =>
        SeoExtractionRules.CharSet(contentType).Should().Be(expected);

    [Fact]
    public void BoundedText_CollapsesWhitespaceAndTrims() =>
        SeoExtractionRules.BoundedText("  Home \n\t  page  ", SeoValueLimits.Title)
            .Should().Be(new SeoValue("Home page", 9));

    [Fact]
    public void BoundedUrl_TrimsButKeepsTheValueAsAuthored() =>
        SeoExtractionRules.BoundedUrl("  /a b/c  ", SeoValueLimits.CanonicalHref)
            .Should().Be(new SeoValue("/a b/c", 6),
                "a canonical href is diagnostic evidence, so internal characters are kept as written");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\n ")]
    public void Bounded_TreatsAnEmptyOrWhitespaceValueAsNoValue(string? raw)
    {
        SeoExtractionRules.BoundedText(raw, SeoValueLimits.Title).Should().Be(SeoValue.None);
        SeoExtractionRules.BoundedUrl(raw, SeoValueLimits.CanonicalHref).Should().Be(SeoValue.None);
    }

    [Fact]
    public void Bounded_TruncatesTheStoredValueButReportsTheObservedLength()
    {
        var bounded = SeoExtractionRules.BoundedText(new string('d', 4000), SeoValueLimits.MetaDescription);

        bounded.Value.Should().HaveLength(SeoValueLimits.MetaDescription);
        bounded.Length.Should().Be(4000, "a truncated stored value must not misreport the real length");
    }

    [Fact]
    public void ResolveAbsolute_KeepsAnAbsoluteHttpUrl() =>
        SeoExtractionRules.ResolveAbsolute("https://example.test/page", "https://other.test/")
            .Should().Be("https://example.test/page");

    [Theory]
    [InlineData("/canonical", "https://example.test/a/b", "https://example.test/canonical")]
    [InlineData("canonical", "https://example.test/a/b", "https://example.test/a/canonical")]
    [InlineData("//cdn.test/x", "https://example.test/a", "https://cdn.test/x")]
    public void ResolveAbsolute_ResolvesRelativeFormsAgainstTheServedUrl(
        string href, string baseUrl, string expected) =>
        SeoExtractionRules.ResolveAbsolute(href, baseUrl).Should().Be(expected);

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>x</h1>")]
    [InlineData("file:///etc/passwd")]
    public void ResolveAbsolute_RejectsNonHttpSchemes(string href) =>
        SeoExtractionRules.ResolveAbsolute(href, "https://example.test/").Should().BeNull();

    [Fact]
    public void ResolveAbsolute_ReturnsNullWhenARelativeHrefHasNoUsableBase() =>
        SeoExtractionRules.ResolveAbsolute("/canonical", null).Should().BeNull();
}
