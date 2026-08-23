using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task ProductionCookieChallengeKeepsAnonymousAjaxPostUnauthorized()
    {
        using var cookieFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                    options.DefaultForbidScheme = IdentityConstants.ApplicationScheme;
                })));
        using var client = cookieFactory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");

        using var response = await client.PostAsync(
            "/Notifications/MarkRead",
            new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task AjaxExceptionResponseUsesProblemDetailsWithCorrelationId()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");

        using var response = await client.GetAsync("/__tests/runtime-error");
        var content = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(content);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(problem.RootElement.TryGetProperty("correlationId", out var correlationId));
        Assert.False(string.IsNullOrWhiteSpace(correlationId.GetString()));
    }

    [Fact]
    public async Task ReversedAuditDatesReturnValidationInsideTheAjaxRegion()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");

        using var response = await client.GetAsync("/Audit?fromDate=2026-08-23&toDate=2026-08-22");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("id=\"ajax-page\"", content, StringComparison.Ordinal);
        Assert.Contains("from date must be on or before", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-shell-validation-summary", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForbiddenAjaxRequestReturnsForbiddenWithoutAccessDeniedNavigation()
    {
        using var client = factory.CreateHttpsClientWithoutRedirects(ApplicationRoles.Viewer);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");

        using var response = await client.GetAsync("/Registry/CreateClient");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task MissingAjaxRecordReturnsNotFoundWithoutAFullLayout()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");

        using var response = await client.GetAsync($"/Registry/Client/{Guid.NewGuid()}");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("<!DOCTYPE html>", content, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/Targets/Endpoints?search=example", ApplicationRoles.Viewer)]
    [InlineData("/Registry/Websites", ApplicationRoles.Viewer)]
    [InlineData("/Incidents?status=Open", ApplicationRoles.Viewer)]
    [InlineData("/Seo?problemsOnly=true", ApplicationRoles.Viewer)]
    [InlineData("/Audit?entity=Endpoint", ApplicationRoles.Administrator)]
    [InlineData("/Crawl", ApplicationRoles.Viewer)]
    [InlineData("/PageAudits", ApplicationRoles.Viewer)]
    public async Task EnhancedReadPagesReturnReplaceableAjaxRegions(string path, string role)
    {
        using var client = factory.CreateHttpsClient(role);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");

        using var response = await client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"ajax-page\"", content, StringComparison.Ordinal);
        Assert.Contains("data-ajax-region", content, StringComparison.Ordinal);
        Assert.DoesNotContain("<!DOCTYPE html>", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHistoryPaginationReturnsAReplaceableAjaxRegion()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");

        using var response = await client.GetAsync($"/Checks/History?id={Guid.NewGuid()}&page=2");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"ajax-page\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("<!DOCTYPE html>", content, StringComparison.OrdinalIgnoreCase);
    }
}
