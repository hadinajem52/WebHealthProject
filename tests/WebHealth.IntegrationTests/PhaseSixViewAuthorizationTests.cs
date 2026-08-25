using System.Net;
using FluentAssertions;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

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

    [Theory]
    [MemberData(nameof(EveryRole))]
    public async Task CrawlRun_OutsideVisibility_IsNotFoundRatherThanForbidden(string role)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/Crawl/Run?id=8a3a1c5e-0000-0000-0000-000000000000");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

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
