using System.Net;
using AngleSharp.Html.Parser;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class RetentionAuthorizationTests(WebHealthWebApplicationFactory factory) : IClassFixture<WebHealthWebApplicationFactory>
{
    [Theory]
    [InlineData(ApplicationRoles.Administrator, HttpStatusCode.OK)]
    [InlineData(ApplicationRoles.Operations, HttpStatusCode.Forbidden)]
    [InlineData(ApplicationRoles.DeveloperSupport, HttpStatusCode.Forbidden)]
    [InlineData(ApplicationRoles.Viewer, HttpStatusCode.Forbidden)]
    public async Task HoldHistoryRequiresAdministrator(string role, HttpStatusCode status)
    {
        using var client = factory.CreateHttpsClientWithoutRedirects(role);
        (await client.GetAsync("/Retention")).StatusCode.Should().Be(status);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Release")]
    public async Task MutationsRequireAntiforgery(string action)
    {
        using var client = factory.CreateHttpsClientWithoutRedirects(ApplicationRoles.Administrator);
        (await client.PostAsync("/Retention/" + action, new FormUrlEncodedContent([])))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(ApplicationRoles.Operations)]
    [InlineData(ApplicationRoles.DeveloperSupport)]
    [InlineData(ApplicationRoles.Viewer)]
    public async Task MutationsRejectOtherRolesWithValidToken(string role)
    {
        using var client = factory.CreateHttpsClientWithoutRedirects(role);
        var shell = await client.GetStringAsync("/");
        var document = await new HtmlParser().ParseDocumentAsync(shell);
        var token = document.QuerySelector("input[name='__RequestVerificationToken']")!.GetAttribute("value")!;
        foreach (var action in new[] { "Create", "Release" })
            (await client.PostAsync("/Retention/" + action, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PageEncodesReasonsAndCreationInterpretsExpiryAsUtc()
    {
        using var client = factory.CreateHttpsClientWithoutRedirects(ApplicationRoles.Administrator);
        var html = await client.GetStringAsync("/Retention");
        html.Should().NotContain("<script>alert('hold')</script>");
        var document = await new HtmlParser().ParseDocumentAsync(html);
        document.QuerySelector(".validation-summary").Should().BeNull();
        document.QuerySelector("td[data-label='Reason']")!.TextContent.Should().Be("<script>alert('hold')</script>");
        var token = document.QuerySelector("input[name='__RequestVerificationToken']")!.GetAttribute("value")!;
        var response = await client.PostAsync("/Retention/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ScopeType"] = "Endpoint",
            ["ScopeId"] = Guid.NewGuid().ToString(),
            ["Reason"] = "Controlled hold",
            ["ExpiresAt"] = "2027-01-02T03:04"
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        factory.Services.GetRequiredService<TestRetentionHoldService>().LastCreated!.ExpiresAt
            .Should().Be(new DateTimeOffset(2027, 1, 2, 3, 4, 0, TimeSpan.Zero));
    }
}
