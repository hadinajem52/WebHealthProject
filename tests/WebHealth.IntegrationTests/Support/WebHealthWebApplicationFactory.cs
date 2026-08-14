using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebHealth.Application.Auditing;

namespace WebHealth.IntegrationTests.Support;

public sealed class WebHealthWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WebHealth"] = string.Empty,
                ["Serilog:MinimumLevel:Default"] = "Fatal"
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuthorizationDenialAuditWriter>();
            services.AddSingleton<RecordingAuthorizationDenialAuditWriter>();
            services.AddSingleton<IAuthorizationDenialAuditWriter>(services =>
                services.GetRequiredService<RecordingAuthorizationDenialAuditWriter>());
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
            services.AddControllersWithViews()
                .AddApplicationPart(typeof(RuntimeFailureController).Assembly);
        });
    }

    public HttpClient CreateHttpsClient(params string[] roles)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.HeaderName, "Test User");
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeaderName, string.Join(',', roles));
        }
        return client;
    }

    public HttpClient CreateAnonymousHttpsClient(bool allowAutoRedirect = true)
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            BaseAddress = new Uri("https://localhost")
        });
    }
}
