using System.Net;
using Xunit;
using WebHealth.IntegrationTests.Support;

namespace WebHealth.IntegrationTests;

public sealed class AuthenticationShellTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    [Fact]
    public async Task OperationalShell_RedirectsAnonymousRequestsToLogin()
    {
        using var client = factory.CreateAnonymousHttpsClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login?returnUrl=%2F", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_IsPublicAndUsesTheAuthenticationLayout()
    {
        using var client = factory.CreateAnonymousHttpsClient();

        var response = await client.GetAsync("/Account/Login");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("class=\"auth-form-panel\"", content, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"username\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Primary\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_DoesNotAcceptAnExternalReturnUrl()
    {
        using var client = factory.CreateAnonymousHttpsClient();

        var content = await client.GetStringAsync(
            "/Account/Login?returnUrl=https%3A%2F%2Fexample.com");

        Assert.Contains("name=\"ReturnUrl\"", content, StringComparison.Ordinal);
        Assert.Contains("value=\"/\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"https://example.com\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginPost_WithoutAntiforgeryToken_IsRejected()
    {
        using var client = factory.CreateAnonymousHttpsClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@example.test",
            ["Password"] = "NotARealPassword1!"
        });

        var response = await client.PostAsync("/Account/Login", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DetailedReadiness_IsProtectedButLivenessRemainsPublic()
    {
        using var client = factory.CreateAnonymousHttpsClient(allowAutoRedirect: false);

        var readiness = await client.GetAsync("/health/ready");
        var liveness = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.Redirect, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
    }

    [Fact]
    public async Task ProtectedShell_ShowsIdentityAndPostOnlySignOut()
    {
        using var client = factory.CreateHttpsClient();

        var content = await client.GetStringAsync("/");

        Assert.Contains("Test User", content, StringComparison.Ordinal);
        Assert.Contains("action=\"/Account/Logout\"", content, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignOut_RejectsGetAndPostWithoutAntiforgeryToken()
    {
        using var client = factory.CreateHttpsClient();

        var getResponse = await client.GetAsync("/Account/Logout");
        var postResponse = await client.PostAsync("/Account/Logout", content: null);

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, postResponse.StatusCode);
    }

    [Fact]
    public async Task AccessDenied_ProducesAForbiddenResponse()
    {
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/Account/AccessDenied");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
