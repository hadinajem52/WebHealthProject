using System.Net;
using FluentAssertions;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class PageAuditSettingsAuthorizationTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    [Fact]
    public async Task Settings_AreReadableByAdministrator()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);

        var response = await client.GetAsync("/PageAuditSettings");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Performance metric thresholds");
        html.Should().Contain("First Contentful Paint");
        html.Should().Contain("Largest Contentful Paint");
        html.Should().Contain("Total Blocking Time");
        html.Should().Contain("Cumulative Layout Shift");
        html.Should().Contain("Speed Index");
    }

    [Theory]
    [InlineData(ApplicationRoles.Operations)]
    [InlineData(ApplicationRoles.DeveloperSupport)]
    [InlineData(ApplicationRoles.Viewer)]
    public async Task Settings_AreRefusedToNonAdministrators(string role)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/PageAuditSettings");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnonymousRequest_IsSentToLogin()
    {
        using var client = factory.CreateAnonymousHttpsClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/PageAuditSettings");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("/Account/Login");
    }

    [Fact]
    public async Task SettingsPost_RequiresAntiForgeryToken()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);

        var response = await client.PostAsync(
            "/PageAuditSettings",
            new FormUrlEncodedContent([new("IncidentsEnabled", "true")]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
