using System.Net;
using System.Text.Json;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using WebHealth.Web.Ajax;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class AjaxContractTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    [Fact]
    public async Task DashboardAjaxRequestReturnsOnlyTheReplaceableRegion()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");

        using var response = await client.GetAsync("/?HealthStatus=Critical");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"ajax-page\"", content, StringComparison.Ordinal);
        Assert.Contains("Critical", content, StringComparison.Ordinal);
        Assert.DoesNotContain("<!DOCTYPE html>", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("id=\"main-content\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NormalDashboardRequestKeepsTheProgressiveEnhancementFallback()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var content = await client.GetStringAsync("/");

        Assert.Contains("<!DOCTYPE html>", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/js/ajax.js", content, StringComparison.Ordinal);
        Assert.Contains("data-ajax-messages", content, StringComparison.Ordinal);
        Assert.Contains("data-ajax-form", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnonymousAjaxRequestReturnsUnauthorizedWithoutLoginMarkup()
    {
        using var client = factory.CreateAnonymousHttpsClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");

        using var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("auth-form-panel", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AjaxExceptionResponseUsesProblemDetailsWithCorrelationId()
    {
        using var client = factory.CreateAnonymousHttpsClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");

        using var response = await client.GetAsync("/Home/Error");
        var content = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(problem.RootElement.TryGetProperty("correlationId", out var correlationId));
        Assert.False(string.IsNullOrWhiteSpace(correlationId.GetString()));
    }
}
