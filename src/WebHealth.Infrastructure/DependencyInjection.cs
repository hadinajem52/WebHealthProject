using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Infrastructure.Diagnostics;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Application.Administration;
using WebHealth.Application.Auditing;
using WebHealth.Infrastructure.Auditing;
using WebHealth.Application.Assignments;
using WebHealth.Infrastructure.Assignments;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Registry;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Monitoring;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Hangfire;
using Hangfire.PostgreSql;

namespace WebHealth.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "WebHealth";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var schedulingOptions = configuration.GetSection(MonitoringSchedulingOptions.SectionName)
            .Get<MonitoringSchedulingOptions>() ?? new MonitoringSchedulingOptions();
        ValidateSchedulingOptions(schedulingOptions);
        services.AddSingleton(schedulingOptions);

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString(DatabaseConnectionName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{DatabaseConnectionName} is not configured.");
            }

            PostgreSqlDbContextOptions.Configure(options, connectionString);
        });

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager<ApplicationUserSignInManager>()
            .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();
        services.Configure<BootstrapAdminOptions>(
            configuration.GetSection(BootstrapAdminOptions.SectionName));
        services.AddScoped<AdminBootstrapper>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<ITeamAdministrationService, TeamAdministrationService>();
        services.AddScoped<IAssignmentAccessEvaluator, AssignmentAccessEvaluator>();
        services.AddScoped<IAuditTrailWriter, AuditTrailWriter>();
        services.AddScoped<IAuditTrailReader, AuditTrailReader>();
        services.AddScoped<IAuthorizationDenialAuditWriter, AuthorizationDenialAuditWriter>();
        services.AddScoped<RegistryVisibility>();
        services.AddScoped<RegistryMutationSupport>();
        services.AddScoped<IRegistryReader, RegistryReader>();
        services.AddScoped<IClientRegistryService, ClientRegistryService>();
        services.AddScoped<IWebsiteRegistryService, WebsiteRegistryService>();
        services.AddScoped<ITargetRegistryReader, TargetRegistryReader>();
        services.AddScoped<IEnvironmentRegistryService, EnvironmentRegistryService>();
        services.AddScoped<IEndpointRegistryService, EndpointRegistryService>();
        services.AddScoped<ITargetAuthorizationService, TargetAuthorizationService>();
        services.AddScoped<IMonitoringEligibilityService, MonitoringEligibilityService>();
        services.AddScoped<IExecutionLeaseService, ExecutionLeaseService>();
        services.AddScoped<ILogicalCheckFinalizationService, LogicalCheckFinalizationService>();
        services.AddScoped<ILogicalCheckExecutionService, LogicalCheckExecutionService>();
        services.AddScoped<IMonitoringSchedulingService, MonitoringSchedulingService>();
        services.AddScoped<IManualCheckService, ManualCheckService>();
        services.AddScoped<ICheckHistoryReader, CheckHistoryReader>();
        services.AddScoped<LogicalCheckJob>();
        services.AddScoped<MonitoringDispatchJob>();
        if (schedulingOptions.Enabled)
        {
            var connectionString = configuration.GetConnectionString(DatabaseConnectionName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{DatabaseConnectionName} is not configured.");
            }

            services.AddHangfire(configurationBuilder => configurationBuilder
                .UsePostgreSqlStorage(bootstrapper => bootstrapper.UseNpgsqlConnection(connectionString),
                    new PostgreSqlStorageOptions
                    {
                        SchemaName = "hangfire",
                        PrepareSchemaIfNecessary = false,
                        QueuePollInterval = TimeSpan.FromSeconds(1)
                    }));
            services.AddHangfireServer(options =>
            {
                options.Queues = [MonitoringQueueNames.ShortChecks];
                options.WorkerCount = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
            });
            services.AddScoped<ILogicalCheckQueue, HangfireLogicalCheckQueue>();
        }
        var configuredUserAgent = configuration[$"{SafeHttpTransportOptions.SectionName}:UserAgent"];
        var safeHttpOptions = new SafeHttpTransportOptions
        {
            UserAgent = string.IsNullOrWhiteSpace(configuredUserAgent)
                ? "WebHealthMonitor/1.0"
                : configuredUserAgent.Trim()
        };
        services.AddSingleton(safeHttpOptions);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMonitoringDnsResolver, SystemMonitoringDnsResolver>();
        services.AddSingleton<IDestinationAddressPolicy, StrictDestinationAddressPolicy>();
        services.AddSingleton<SafeHttpConcurrencyLimiter>();
        services.AddScoped<IMonitoringTargetAuthorizer, MonitoringTargetAuthorizer>();
        services.AddScoped<ISafeHttpTransport, SafeHttpTransport>();
        services.AddHttpClient(SafeHttpTransportOptions.ClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(safeHttpOptions.UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
                SafeHttpConnectionFactory.Create(
                    serviceProvider.GetRequiredService<IMonitoringDnsResolver>(),
                    serviceProvider.GetRequiredService<IDestinationAddressPolicy>(),
                    serviceProvider.GetRequiredService<SafeHttpConcurrencyLimiter>(),
                    safeHttpOptions));

        services.AddHealthChecks()
            .AddCheck<PostgreSqlReadinessCheck>("postgresql", tags: ["ready"]);

        return services;
    }

    private static void ValidateSchedulingOptions(MonitoringSchedulingOptions options)
    {
        if (options.DispatchBatchSize is < 1 or > 500
            || options.RecoveryBatchSize is < 1 or > 1000
            || options.RecoveryDelay < TimeSpan.FromMinutes(1)
            || options.RecoveryDelay > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException("Monitoring scheduling options are outside their safe bounds.");
        }
    }
}
