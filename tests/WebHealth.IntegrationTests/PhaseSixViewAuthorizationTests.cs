using System.Net;
using FluentAssertions;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// Phase 6 increment 6.8. Every role's **direct** request to the SEO and broken-link views, made
/// against the real routes rather than against a synthetic policy endpoint — a navigation entry
/// that hides a link is a usability choice, and only the request itself proves the server refuses.
/// </summary>
public sealed class PhaseSixViewAuthorizationTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    public static TheoryData<string> EveryRole =>
    [
        ApplicationRoles.Administrator,
        ApplicationRoles.Operations,
        ApplicationRoles.DeveloperSupport,
        ApplicationRoles.Viewer
    ];

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task Seo_IsReadableByEveryApplicationPersona(string role)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/Seo");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an SEO observation is a read surface for every persona that may read the registry");
    }

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task BrokenLinks_AreReadableByEveryApplicationPersona(string role)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/Crawl");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/Seo")]
    [InlineData("/Crawl")]
    [InlineData("/Crawl/Run?id=8a3a1c5e-0000-0000-0000-000000000000")]
    public async Task AnonymousRequest_IsSentToLogin(string path)
    {
        using var client = factory.CreateAnonymousHttpsClient(allowAutoRedirect: false);

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("/Account/Login");
    }

    /// <summary>
    /// A run the requester may not see is <c>404</c>, not <c>403</c>. Answering "forbidden" would
    /// confirm that the run exists, which is itself a disclosure about another client's data.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task CrawlRun_OutsideVisibility_IsNotFoundRatherThanForbidden(string role)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/Crawl/Run?id=8a3a1c5e-0000-0000-0000-000000000000");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The filters are applied by the reader, so a hand-written query string cannot widen what is
    /// returned — an unrecognised value is treated as no filter rather than rejected or passed
    /// through to the database.
    /// </summary>
    [Theory]
    [InlineData("/Seo?applicability=Everything")]
    [InlineData("/Seo?environment=Staging%20AND%201%3D1")]
    [InlineData("/Seo?page=-5")]
    [InlineData("/Crawl?endpointId=not-a-guid")]
    public async Task UnrecognisedFilterValues_DoNotFail(string path)
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
