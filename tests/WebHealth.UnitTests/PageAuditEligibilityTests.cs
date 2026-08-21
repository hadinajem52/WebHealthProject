using FluentAssertions;
using WebHealth.Domain.PageAudits;
using Xunit;

namespace WebHealth.UnitTests;

/// <summary>
/// The public-only rule. A wrong answer here is not a failed check but a disclosure: an internal
/// URL handed to Google and loaded by their infrastructure. So the tests below are written around
/// what the rule must refuse, not around what it may allow.
/// </summary>
public sealed class PageAuditEligibilityTests
{
    private static PageAuditEligibilityResult Evaluate(string? url) =>
        PageAuditEligibility.Evaluate(url);

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://example.co.uk/")]
    [InlineData("https://sub.domain.example.org/path")]
    public void Evaluate_AcceptsAPublicHttpOrHttpsPage(string url) =>
        Evaluate(url).IsEligible.Should().BeTrue();

    [Fact]
    public void Evaluate_AcceptsAPublicLiteralAddress() =>
        Evaluate("https://93.184.216.34/").IsEligible.Should().BeTrue();

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("https://localhost:5001/health")]
    public void Evaluate_RejectsLocalhost(string url) =>
        Evaluate(url).Reason.Should().Be(PageAuditIneligibilityReasons.HostNotPublic);

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://192.168.1.10/")]
    [InlineData("http://172.16.4.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    public void Evaluate_RejectsAnAddressThePublicInternetCannotReach(string url) =>
        Evaluate(url).Reason.Should().Be(PageAuditIneligibilityReasons.AddressNotPublic);

    [Theory]
    [InlineData("http://intranet/")]
    [InlineData("http://buildserver:8080/")]
    public void Evaluate_RejectsASingleLabelHost(string url) =>
        Evaluate(url).Reason.Should().Be(PageAuditIneligibilityReasons.HostNotPublic,
            "a single label has no public registry behind it, so only this network resolves it");

    [Theory]
    [InlineData("http://printer.local/")]
    [InlineData("http://app.internal/")]
    [InlineData("https://staging.corp/")]
    [InlineData("http://box.home.arpa/")]
    [InlineData("http://service.test/")]
    public void Evaluate_RejectsAReservedInternalSuffix(string url) =>
        Evaluate(url).Reason.Should().Be(PageAuditIneligibilityReasons.HostNotPublic);

    [Theory]
    [InlineData("ftp://example.com/")]
    [InlineData("file:///c:/site/index.html")]
    public void Evaluate_RejectsASchemeTheProviderCannotAudit(string url) =>
        Evaluate(url).Reason.Should().Be(PageAuditIneligibilityReasons.SchemeNotSupported);

    /// <summary>
    /// A query is where a signed link, a reset token or a session identifier lives, and nothing
    /// here can tell one of those from a locale switch. Refusing the whole class costs an endpoint
    /// that cannot be audited; allowing it costs a secret handed to a third party who then loads it.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/reset?token=SECRET")]
    [InlineData("https://example.com/?lang=en")]
    [InlineData("https://example.com/doc?a=1&sig=abc")]
    public void Evaluate_RejectsAUrlCarryingAQueryString(string url) =>
        Evaluate(url).Reason.Should().Be(PageAuditIneligibilityReasons.UrlCarriesQuery);

    [Fact]
    public void Evaluate_RejectsAUrlCarryingCredentials() =>
        Evaluate("https://user:secret@example.com/").Reason
            .Should().Be(PageAuditIneligibilityReasons.UrlCarriesCredentials,
                "the credentials would be handed to the provider along with the URL");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/relative/path")]
    [InlineData("not a url")]
    public void Evaluate_RejectsAnythingThatIsNotAnAbsoluteUrl(string? url) =>
        Evaluate(url).Reason.Should().Be(PageAuditIneligibilityReasons.UrlNotAbsolute);

    [Fact]
    public void Evaluate_MatchesAnInternalSuffixThroughItsPunycodeForm() =>
        Evaluate("http://münchen.local/").Reason
            .Should().Be(PageAuditIneligibilityReasons.HostNotPublic,
                "a unicode host and its punycode form are the same host");

    [Fact]
    public void Evaluate_IgnoresATrailingRootDotWhenMatchingASuffix() =>
        Evaluate("http://printer.local./").Reason
            .Should().Be(PageAuditIneligibilityReasons.HostNotPublic);

    [Fact]
    public void Evaluate_CarriesNoReasonWhenItAccepts() =>
        Evaluate("https://example.com/").Reason.Should().BeNull();
}
