using System.Net;
using Microsoft.AspNetCore.Mvc;
using WebHealth.IntegrationTests.Support;
using WebHealth.Web.Middleware;
using Xunit;
using WebHealth.Infrastructure.Identity;

namespace WebHealth.IntegrationTests;

public sealed class RuntimeFoundationTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    [Fact]
    public async Task Liveness_IsHealthyWithoutDatabaseConfiguration()
    {
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_IsUnhealthyWithoutDatabaseConfiguration()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Operations);

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Responses_IncludeServerGeneratedCorrelationId()
    {
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/health/live");

        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(values)));
    }

    [Fact]
    public async Task ErrorPage_IsSafeAndContainsCorrelationReference()
    {
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/Home/HttpStatusCode?code=404");
        var content = await response.Content.ReadAsStringAsync();
        var correlationId = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(correlationId, content);
        Assert.DoesNotContain("Development Mode", content);
    }

    [Fact]
    public async Task UnhandledException_DoesNotExposeExceptionDetails()
    {
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/__tests/runtime-error");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(RuntimeFailureController.SensitiveCanary, content);
        Assert.DoesNotContain(nameof(InvalidOperationException), content);
    }

}

[ApiController]
[Route("__tests/runtime-error")]
public sealed class RuntimeFailureController : ControllerBase
{
    public const string SensitiveCanary = "sensitive-exception-canary";

    [HttpGet]
    public IActionResult Get()
    {
        throw new InvalidOperationException(SensitiveCanary);
    }
}
