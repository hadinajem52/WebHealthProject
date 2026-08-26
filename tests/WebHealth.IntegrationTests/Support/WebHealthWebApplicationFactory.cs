using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebHealth.Application.Auditing;
using WebHealth.Application.Administration;
using WebHealth.Application.Assignments;
using WebHealth.Application.Incidents;
using WebHealth.Application.Maintenance;
using WebHealth.Application.Notifications;
using WebHealth.Application.Seo;
using WebHealth.Application.Crawling;
using WebHealth.Application.PageAudits;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;
using WebHealth.Application.Monitoring;

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
            services.RemoveAll<INotificationFeedReader>();
            services.AddScoped<INotificationFeedReader, EmptyNotificationFeedReader>();

            services.RemoveAll<IReportingReader>();
            services.AddScoped<IReportingReader, EmptyReportingReader>();
            services.RemoveAll<IRegistryReader>();
            services.AddScoped<IRegistryReader, EmptyRegistryReader>();
            services.RemoveAll<IIncidentReader>();
            services.AddScoped<IIncidentReader, EmptyIncidentReader>();
            services.RemoveAll<ITargetRegistryReader>();
            services.AddScoped<ITargetRegistryReader, EmptyTargetRegistryReader>();

            services.RemoveAll<ISeoReader>();
            services.AddScoped<ISeoReader, EmptySeoReader>();
            services.RemoveAll<ICrawlReportReader>();
            services.AddScoped<ICrawlReportReader, EmptyCrawlReportReader>();
            services.RemoveAll<IPageAuditReader>();
            services.AddScoped<IPageAuditReader, EmptyPageAuditReader>();
            services.RemoveAll<IPageAuditIncidentPolicyService>();
            services.AddSingleton<EmptyPageAuditIncidentPolicyService>();
            services.AddScoped<IPageAuditIncidentPolicyService>(provider =>
                provider.GetRequiredService<EmptyPageAuditIncidentPolicyService>());
            services.RemoveAll<IAuditTrailReader>();
            services.AddScoped<IAuditTrailReader, EmptyAuditTrailReader>();
            services.RemoveAll<ICheckHistoryReader>();
            services.AddScoped<ICheckHistoryReader, EmptyCheckHistoryReader>();
            services.RemoveAll<IClientRegistryService>();
            services.RemoveAll<IWebsiteRegistryService>();
            services.RemoveAll<IEnvironmentRegistryService>();
            services.RemoveAll<IEndpointRegistryService>();
            services.RemoveAll<IEndpointRegistrationService>();
            services.AddScoped<EmptyRegistryMutationServices>();
            services.AddScoped<IClientRegistryService>(provider => provider.GetRequiredService<EmptyRegistryMutationServices>());
            services.AddScoped<IWebsiteRegistryService>(provider => provider.GetRequiredService<EmptyRegistryMutationServices>());
            services.AddScoped<IEnvironmentRegistryService>(provider => provider.GetRequiredService<EmptyRegistryMutationServices>());
            services.AddScoped<IEndpointRegistryService>(provider => provider.GetRequiredService<EmptyRegistryMutationServices>());
            services.AddScoped<IEndpointRegistrationService>(provider => provider.GetRequiredService<EmptyRegistryMutationServices>());
            services.RemoveAll<IIncidentLifecycleService>();
            services.AddScoped<IIncidentLifecycleService, EmptyIncidentLifecycleService>();
            services.RemoveAll<IManualCheckService>();
            services.AddScoped<IManualCheckService, EmptyManualCheckService>();
            services.RemoveAll<IMaintenanceReader>();
            services.AddScoped<IMaintenanceReader, EmptyMaintenanceReader>();
            services.RemoveAll<IMaintenanceWindowService>();
            services.AddScoped<IMaintenanceWindowService, EmptyMaintenanceWindowService>();
            services.RemoveAll<IUserAdministrationService>();
            services.AddScoped<IUserAdministrationService, EmptyUserAdministrationService>();
            services.RemoveAll<ITeamAdministrationService>();
            services.AddScoped<ITeamAdministrationService, EmptyTeamAdministrationService>();

            services.RemoveAll<IPageAuditRunner>();
            services.AddSingleton<RecordingPageAuditRunner>();
            services.AddScoped<IPageAuditRunner>(provider =>
                provider.GetRequiredService<RecordingPageAuditRunner>());
            services.RemoveAll<ICrawlRunner>();
            services.AddSingleton<RecordingCrawlRunner>();
            services.AddScoped<ICrawlRunner>(provider =>
                provider.GetRequiredService<RecordingCrawlRunner>());
            services.RemoveAll<IEndpointTestGate>();
            services.AddScoped<IEndpointTestGate, PermissiveEndpointTestGate>();

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
        return CreateHttpsClient(true, roles);
    }

    public HttpClient CreateHttpsClientWithoutRedirects(params string[] roles)
    {
        return CreateHttpsClient(false, roles);
    }

    private HttpClient CreateHttpsClient(bool allowAutoRedirect, string[] roles)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
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
