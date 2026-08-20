using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebHealth.Application.Auditing;
using WebHealth.Application.Incidents;
using WebHealth.Application.Notifications;
using WebHealth.Application.Seo;
using WebHealth.Application.Crawling;
using WebHealth.Application.PageAudits;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;

namespace WebHealth.IntegrationTests.Support;

public sealed class WebHealthWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WebHealth"] = string.Empty,
                ["Monitoring:Scheduling:Enabled"] = "false",
                ["Serilog:MinimumLevel:Default"] = "Fatal"
            }));

        builder.ConfigureServices(services =>
        {
            // The shell renders the notification panel on every page. These tests run with no
            // database, so the feed is stubbed rather than reaching for a DbContext.
            services.RemoveAll<INotificationFeedReader>();
            services.AddScoped<INotificationFeedReader, EmptyNotificationFeedReader>();

            // The dashboard is a real read surface as of increment 5.6, so its readers are
            // stubbed for the same reason: these tests exercise the shell, not the data.
            services.RemoveAll<IReportingReader>();
            services.AddScoped<IReportingReader, EmptyReportingReader>();
            services.RemoveAll<IRegistryReader>();
            services.AddScoped<IRegistryReader, EmptyRegistryReader>();
            services.RemoveAll<IIncidentReader>();
            services.AddScoped<IIncidentReader, EmptyIncidentReader>();
            services.RemoveAll<ITargetRegistryReader>();
            services.AddScoped<ITargetRegistryReader, EmptyTargetRegistryReader>();

            // The Phase 6 views read through their own readers, stubbed here for the same reason.
            services.RemoveAll<ISeoReader>();
            services.AddScoped<ISeoReader, EmptySeoReader>();
            services.RemoveAll<ICrawlReportReader>();
            services.AddScoped<ICrawlReportReader, EmptyCrawlReportReader>();
            services.RemoveAll<IPageAuditReader>();
            services.AddScoped<IPageAuditReader, EmptyPageAuditReader>();

            // The PageSpeed page decides whether to offer Run now, and the action itself checks
            // the same thing. Both reach the database in production, so both are stubbed here.
            services.RemoveAll<IPageAuditRunner>();
            services.AddSingleton<RecordingPageAuditRunner>();
            services.AddScoped<IPageAuditRunner>(provider =>
                provider.GetRequiredService<RecordingPageAuditRunner>());
            services.RemoveAll<ITargetAuthorizationService>();
            services.AddScoped<ITargetAuthorizationService, PermissiveTargetAuthorizationService>();

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
