using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebHealth.IntegrationTests.Support;

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "IntegrationTest";
    public const string HeaderName = "X-WebHealth-Test-User";
    public const string RolesHeaderName = "X-WebHealth-Test-Roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var name)
            || string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.Empty.ToString()),
            new(ClaimTypes.Name, name!)
        };
        if (Request.Headers.TryGetValue(RolesHeaderName, out var roleValues))
        {
            claims.AddRange(roleValues
                .SelectMany(value => value?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [])
                .Select(role => new Claim(ClaimTypes.Role, role.Trim())));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var returnUrl = $"{Request.PathBase}{Request.Path}{Request.QueryString}";
        Response.Redirect($"/Account/Login{QueryString.Create("returnUrl", returnUrl)}");
        return Task.CompletedTask;
    }
}
