using FluentAssertions;
using WebHealth.Application.Registry;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class EndpointUrlResolverTests
{
    private sealed class StubProbe(Dictionary<string, EndpointSchemeProbeOutcome> outcomes)
        : IEndpointUrlSchemeProbe
    {
        public List<string> Attempts { get; } = [];

        public Task<EndpointSchemeProbeOutcome> ProbeAsync(
            string url, CancellationToken cancellationToken = default)
        {
            Attempts.Add(url);
            return Task.FromResult(outcomes.TryGetValue(url, out var outcome)
                ? outcome
                : EndpointSchemeProbeOutcome.SchemeUnavailable);
        }
    }

    [Fact]
    public async Task ResolveAsync_PrefersHttpsAndNeverProbesHttpWhenHttpsAnswers()
    {
        var probe = new StubProbe(new()
        {
            ["https://google.com/"] = EndpointSchemeProbeOutcome.Responded
        });

        var result = await new EndpointUrlResolver(probe).ResolveAsync("google.com");

        result.Value.Should().Be("https://google.com/");
        result.Source.Should().Be(EndpointUrlSchemeSource.Detected);
        probe.Attempts.Should().Equal("https://google.com/");
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToHttpWhenHttpsIsRefusedOnThatSchemeAlone()
    {
        var probe = new StubProbe(new()
        {
            ["https://legacy.example/"] = EndpointSchemeProbeOutcome.SchemeUnavailable,
            ["http://legacy.example/"] = EndpointSchemeProbeOutcome.Responded
        });

        var result = await new EndpointUrlResolver(probe).ResolveAsync("legacy.example");

        result.Value.Should().Be("http://legacy.example/");
        result.Source.Should().Be(EndpointUrlSchemeSource.Detected);
        probe.Attempts.Should().Equal("https://legacy.example/", "http://legacy.example/");
    }

    [Fact]
    public async Task ResolveAsync_StopsAfterOneAttemptWhenTheHostItselfIsUnavailable()
    {
        var probe = new StubProbe(new()
        {
            ["https://offline.example/"] = EndpointSchemeProbeOutcome.HostUnavailable
        });

        var result = await new EndpointUrlResolver(probe).ResolveAsync("offline.example");

        result.Value.Should().Be("https://offline.example/");
        result.Source.Should().Be(EndpointUrlSchemeSource.Assumed);
        probe.Attempts.Should().Equal("https://offline.example/");
    }

    [Fact]
    public async Task ResolveAsync_AssumesHttpsWhenNeitherSchemeAnswers()
    {
        var probe = new StubProbe([]);

        var result = await new EndpointUrlResolver(probe).ResolveAsync("silent.example");

        result.Value.Should().Be("https://silent.example/");
        result.Source.Should().Be(EndpointUrlSchemeSource.Assumed);
        probe.Attempts.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("https://www.google.com/")]
    [InlineData("ftp://example.com/file")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ResolveAsync_PassesExplicitOrUnusableInputThroughWithoutProbing(string? value)
    {
        var probe = new StubProbe([]);

        var result = await new EndpointUrlResolver(probe).ResolveAsync(value);

        result.Value.Should().Be(value);
        result.Source.Should().Be(EndpointUrlSchemeSource.AsTyped);
        probe.Attempts.Should().BeEmpty();
    }

    [Fact]
    public void Coerce_AddsHttpsWithoutAnyNetworkCall()
    {
        var result = EndpointUrlResolver.Coerce("www.google.com");

        result.Value.Should().Be("https://www.google.com/");
        result.Source.Should().Be(EndpointUrlSchemeSource.Assumed);
    }

    [Theory]
    [InlineData("http://legacy.example/")]
    [InlineData(null)]
    public void Coerce_LeavesInputWithAnExplicitSchemeAlone(string? value)
    {
        var result = EndpointUrlResolver.Coerce(value);

        result.Value.Should().Be(value);
        result.Source.Should().Be(EndpointUrlSchemeSource.AsTyped);
    }
}
