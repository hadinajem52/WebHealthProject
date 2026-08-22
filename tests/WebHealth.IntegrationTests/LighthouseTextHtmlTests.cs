using System.Text.Encodings.Web;
using FluentAssertions;
using WebHealth.Web.Shell;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// Google's audit descriptions arrive over an outbound HTTP call, so they are untrusted input
/// that this application renders as markup. These tests pin both halves of that: the markdown
/// subset Lighthouse actually uses is rendered, and nothing else in the string can become markup
/// or an executable URL.
/// </summary>
public sealed class LighthouseTextHtmlTests
{
    private static string Render(string? value)
    {
        using var writer = new StringWriter();
        LighthouseTextHtml.LighthouseText(html: null!, value).WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    [Fact]
    public void MarkdownLink_BecomesAnAnchorThatCannotReachBackIntoThisTab()
    {
        var html = Render("[Learn more](https://developer.chrome.com/docs/lighthouse/seo/canonical/)");

        html.Should().Be(
            "<a href=\"https://developer.chrome.com/docs/lighthouse/seo/canonical/\" "
            + "target=\"_blank\" rel=\"noopener noreferrer nofollow\">Learn more</a>");
    }

    [Fact]
    public void CodeSpan_BecomesCodeRatherThanLiteralBackticks()
    {
        Render("Document has a valid `rel=canonical`")
            .Should().Be("Document has a valid <code>rel=canonical</code>");
    }

    [Fact]
    public void CodeSpanInsideALinkLabel_IsRenderedInsideTheAnchor()
    {
        var html = Render("[Learn more about the `alt` attribute](https://example.com/alt)");

        html.Should().Be(
            "<a href=\"https://example.com/alt\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">"
            + "Learn more about the <code>alt</code> attribute</a>");
    }

    [Fact]
    public void ProseAroundAConstruct_Survives()
    {
        var html = Render("Descriptive link text helps. [Learn how](https://example.com/x). Really.");

        html.Should().Be(
            "Descriptive link text helps. "
            + "<a href=\"https://example.com/x\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">Learn how</a>"
            + ". Really.");
    }

    /// <summary>
    /// The reason this renderer exists at all is that it emits raw markup. Anything that is not
    /// one of the two recognised constructs has to come out encoded, or a description containing
    /// a tag would become that tag.
    /// </summary>
    [Fact]
    public void Markup_InThePlainText_IsEncoded()
    {
        Render("<script>alert('x')</script> & \"quoted\"")
            .Should().NotContain("<script>")
            .And.StartWith("&lt;script&gt;");
    }

    [Fact]
    public void Markup_InALinkLabel_IsEncoded()
    {
        var html = Render("[<img src=x onerror=alert(1)>](https://example.com/)");

        html.Should().NotContain("<img");
        html.Should().Contain("&lt;img");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("/relative/path")]
    [InlineData("//example.com/protocol-relative")]
    [InlineData("not a url at all")]
    public void NonHttpTarget_NeverBecomesAnHref(string target)
    {
        var html = Render($"[Click me]({target})");

        html.Should().NotContain("<a ");
        html.Should().NotContain("href");
        html.Should().Contain("Click me");
    }

    /// <summary>
    /// A refused target is still shown, so a reader can see what the provider returned instead of
    /// a sentence with a hole where the link should have been.
    /// </summary>
    [Fact]
    public void RefusedTarget_IsStillVisibleAsText()
    {
        Render("[Click me](javascript:alert(1))")
            .Should().Contain("Click me");
    }

    [Fact]
    public void UnterminatedConstruct_StaysLiteralInsteadOfSwallowingTheRest()
    {
        Render("[Learn more](https://example.com  and then some text")
            .Should().Contain("Learn more");

        Render("An `unclosed code span")
            .Should().Be("An `unclosed code span");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AbsentValue_RendersNothing(string? value) =>
        Render(value).Should().BeEmpty();

    [Fact]
    public void Fallback_IsUsedForAnAbsentValueAndIsItselfEncoded()
    {
        using var writer = new StringWriter();
        LighthouseTextHtml.LighthouseText(html: null!, null, "<none>").WriteTo(writer, HtmlEncoder.Default);

        writer.ToString().Should().Be("&lt;none&gt;");
    }

    [Fact]
    public void SeveralConstructs_InOneStringAreAllRendered()
    {
        var html = Render("Set `rel=canonical`, see [docs](https://example.com/a) and `hreflang`.");

        html.Should().Contain("<code>rel=canonical</code>");
        html.Should().Contain("<code>hreflang</code>");
        html.Should().Contain("<a href=\"https://example.com/a\"");
    }
}
