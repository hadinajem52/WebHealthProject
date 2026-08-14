using System.Net;
using FluentAssertions;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace WebHealth.IntegrationTests;

public sealed class AuthorizationBaselineTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    [Fact]
    public async Task AnonymousAdministrationRequest_RedirectsToLogin()
    {
        using var client = factory.CreateAnonymousHttpsClient(allowAutoRedirect: false);

        var response = await client.GetAsync("/Administration/Users");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("/Account/Login");
    }

    [Theory]
    [InlineData(ApplicationRoles.Operations)]
    [InlineData(ApplicationRoles.DeveloperSupport)]
    [InlineData(ApplicationRoles.Viewer)]
    public async Task NonAdministrator_CannotRequestAdministrationDirectly(string role)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/test/authorization/administration");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Administrator_CanRequestAdministrationPolicy()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Administrator);

        var response = await client.GetAsync("/test/authorization/administration");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(ApplicationRoles.Administrator, HttpStatusCode.OK)]
    [InlineData(ApplicationRoles.Operations, HttpStatusCode.OK)]
    [InlineData(ApplicationRoles.DeveloperSupport, HttpStatusCode.Forbidden)]
    [InlineData(ApplicationRoles.Viewer, HttpStatusCode.Forbidden)]
    public async Task OperatePolicy_UsesGlobalOperationalRoles(string role, HttpStatusCode expected)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/test/authorization/operate");

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData(ApplicationRoles.Administrator, HttpStatusCode.OK)]
    [InlineData(ApplicationRoles.Operations, HttpStatusCode.OK)]
    [InlineData(ApplicationRoles.DeveloperSupport, HttpStatusCode.Forbidden)]
    [InlineData(ApplicationRoles.Viewer, HttpStatusCode.Forbidden)]
    public async Task ReadAllPolicy_FailsClosedForAssignmentBoundRoles(
        string role,
        HttpStatusCode expected)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/test/authorization/read-all");

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData(ApplicationRoles.Administrator, HttpStatusCode.OK)]
    [InlineData(ApplicationRoles.Operations, HttpStatusCode.OK)]
    [InlineData(ApplicationRoles.DeveloperSupport, HttpStatusCode.Forbidden)]
    [InlineData(ApplicationRoles.Viewer, HttpStatusCode.Forbidden)]
    public async Task AuditHistory_UsesPrivilegedReadPolicy(string role, HttpStatusCode expected)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/test/authorization/audit-history");

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData(ApplicationRoles.Administrator)]
    [InlineData(ApplicationRoles.Operations)]
    [InlineData(ApplicationRoles.DeveloperSupport)]
    [InlineData(ApplicationRoles.Viewer)]
    public async Task RegistryRead_AllowsEveryApplicationPersona(string role)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/test/authorization/registry-read");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(ApplicationRoles.Administrator, HttpStatusCode.OK)]
    [InlineData(ApplicationRoles.Operations, HttpStatusCode.OK)]
    [InlineData(ApplicationRoles.DeveloperSupport, HttpStatusCode.Forbidden)]
    [InlineData(ApplicationRoles.Viewer, HttpStatusCode.Forbidden)]
    public async Task RegistryManage_UsesPrivilegedRoles(string role, HttpStatusCode expected)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/test/authorization/registry-manage");

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData(ApplicationRoles.Administrator, HttpStatusCode.ServiceUnavailable)]
    [InlineData(ApplicationRoles.Operations, HttpStatusCode.ServiceUnavailable)]
    [InlineData(ApplicationRoles.DeveloperSupport, HttpStatusCode.Forbidden)]
    [InlineData(ApplicationRoles.Viewer, HttpStatusCode.Forbidden)]
    public async Task DetailedReadiness_UsesDiagnosticsRolePolicy(
        string role,
        HttpStatusCode expected)
    {
        using var client = factory.CreateHttpsClient(role);

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task RolelessAuthenticatedPrincipal_CannotReadDetailedReadiness()
    {
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuthenticatedForbiddenRequest_IsSentToTheAuditWriter()
    {
        using var client = factory.CreateHttpsClient(ApplicationRoles.Viewer);
        var auditWriter = factory.Services.GetRequiredService<RecordingAuthorizationDenialAuditWriter>();

        var response = await client.GetAsync("/test/authorization/audit-denial?secret=not-audited");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        auditWriter.Entries.Should().Contain(entry =>
            entry.ActorUserId == Guid.Empty
            && entry.RequestMethod == "GET"
            && entry.RequestPath == "/test/authorization/audit-denial"
            && !entry.RequestPath.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UserNavigation_IsVisibleOnlyToAdministrators()
    {
        using var administrator = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        using var viewer = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        var administratorHtml = await administrator.GetStringAsync("/");
        var viewerHtml = await viewer.GetStringAsync("/");

        administratorHtml.Should().Contain("href=\"/Administration/Users\"");
        administratorHtml.Should().Contain("href=\"/Administration/Teams\"");
        administratorHtml.Should().Contain("href=\"/Audit\"");
        administratorHtml.Should().Contain("href=\"/Registry/Clients\"");
        viewerHtml.Should().Contain("href=\"/Registry/Clients\"");
        viewerHtml.Should().NotContain("href=\"/Administration/Users\"");
        viewerHtml.Should().NotContain("href=\"/Administration/Teams\"");
        viewerHtml.Should().NotContain("href=\"/Audit\"");
    }
}
