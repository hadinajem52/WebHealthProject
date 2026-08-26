using FluentAssertions;
using WebHealth.Domain.Normalization;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class EndpointUrlInputTests
{
    [Theory]
    [InlineData("google.com", "https://google.com/", "http://google.com/")]
    [InlineData("www.google.com", "https://www.google.com/", "http://www.google.com/")]
    [InlineData("  Google.COM/health?a=1  ", "https://google.com/health?a=1", "http://google.com/health?a=1")]
    [InlineData("//google.com", "https://google.com/", "http://google.com/")]
    [InlineData("example.com:8443/status", "https://example.com:8443/status", "http://example.com:8443/status")]
    [InlineData("bücher.example", "https://xn--bcher-kva.example/", "http://xn--bcher-kva.example/")]
    public void Interpret_BuildsBothCandidatesWhenSchemeIsMissing(
        string value, string https, string http)
    {
        var result = EndpointUrlInput.Interpret(value);

        result.Kind.Should().Be(EndpointUrlInputKind.SchemeMissing);
        result.NeedsSchemeProbe.Should().BeTrue();
        result.HttpsCandidate.Should().Be(https);
        result.HttpCandidate.Should().Be(http);
    }

    [Theory]
    [InlineData("https://www.google.com/")]
    [InlineData("http://example.com/health")]
    [InlineData("HTTPS://example.com/")]
    [InlineData("ftp://example.com/file")]
    [InlineData("/relative")]
    public void Interpret_LeavesInputAloneWhenASchemeIsPresentOrTheValueIsRelative(string value)
    {
        var result = EndpointUrlInput.Interpret(value);

        result.Kind.Should().Be(EndpointUrlInputKind.SchemeProvided);
        result.NeedsSchemeProbe.Should().BeFalse();
    }

    [Fact]
    public void Interpret_ReportsEmptyInput()
    {
        EndpointUrlInput.Interpret("   ").Kind.Should().Be(EndpointUrlInputKind.Empty);
        EndpointUrlInput.Interpret(null).Kind.Should().Be(EndpointUrlInputKind.Empty);
    }

    [Fact]
    public void Interpret_YieldsNoCandidatesWhenTheHostIsUnusable()
    {
        EndpointUrlInput.Interpret("user:secret@example.com").NeedsSchemeProbe.Should().BeFalse();
    }
}
