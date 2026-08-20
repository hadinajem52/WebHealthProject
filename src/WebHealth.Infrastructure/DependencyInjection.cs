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
using WebHealth.Application.Reporting;
using WebHealth.Infrastructure.Registry;
using WebHealth.Infrastructure.Reporting;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Application.Maintenance;
using WebHealth.Application.Seo;
using WebHealth.Infrastructure.Maintenance;
using WebHealth.Infrastructure.Seo;
using WebHealth.Application.Crawling;
using WebHealth.Infrastructure.Crawling;
using WebHealth.Application.PageAudits;
using WebHealth.Infrastructure.PageAudits;
using WebHealth.Application.Incidents;
using WebHealth.Infrastructure.Incidents;
using WebHealth.Application.Notifications;
using WebHealth.Infrastructure.Notifications;
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

        var notificationOptions = configuration.GetSection(NotificationSchedulingOptions.SectionName)
            .Get<NotificationSchedulingOptions>() ?? new NotificationSchedulingOptions();
        ValidateNotificationOptions(notificationOptions);
        services.AddSingleton(notificationOptions);

        var seoOptions = configuration.GetSection(SeoSchedulingOptions.SectionName)
            .Get<SeoSchedulingOptions>() ?? new SeoSchedulingOptions();
        ValidateSeoOptions(seoOptions);
        services.AddSingleton(seoOptions);

        var crawlOptions = configuration.GetSection(CrawlSchedulingOptions.SectionName)
            .Get<CrawlSchedulingOptions>() ?? new CrawlSchedulingOptions();
        services.AddSingleton(crawlOptions);

        var pageAuditOptions = configuration.GetSection(PageAuditSchedulingOptions.SectionName)
            .Get<PageAuditSchedulingOptions>() ?? new PageAuditSchedulingOptions();
        var pageSpeedOptions = configuration.GetSection(PageSpeedInsightsOptions.SectionName)
            .Get<PageSpeedInsightsOptions>() ?? new PageSpeedInsightsOptions();
        ValidatePageAuditOptions(pageAuditOptions, pageSpeedOptions);
        services.AddSingleton(pageAuditOptions);
        services.AddSingleton(pageSpeedOptions);

        var maintenanceOptions = configuration.GetSection(MaintenanceSchedulingOptions.SectionName)
            .Get<MaintenanceSchedulingOptions>() ?? new MaintenanceSchedulingOptions();
        ValidateMaintenanceOptions(maintenanceOptions);
        services.AddSingleton(maintenanceOptions);

        var smtpOptions = configuration.GetSection(SmtpEmailOptions.SectionName)
            .Get<SmtpEmailOptions>() ?? new SmtpEmailOptions();
        if (smtpOptions.Enabled)
        {
            ValidateSmtpOptions(smtpOptions);
            services.AddSingleton(smtpOptions);
            // Registered before the recording fallback below, which uses TryAdd.
            services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
        }

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
        services.AddScoped<EndpointPurgeCascade>();
        services.AddScoped<WebsitePurgeCascade>();
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
        services.AddScoped<OwnerSubjectNames>();
        services.AddScoped<IReportingReader, ReportingReader>();
        services.AddScoped<IMaintenanceWindowService, MaintenanceWindowService>();
        services.AddScoped<IMaintenanceReader, MaintenanceReader>();
        services.AddScoped<IMaintenanceEvaluator, MaintenanceEvaluator>();
        services.AddSingleton<ISeoValueExtractor, SeoValueExtractor>();
        services.AddScoped<ISeoReader, SeoReader>();
        services.AddScoped<RobotsRefreshService>();
        services.AddScoped<IRobotsPolicyService, RobotsPolicyService>();
        services.AddScoped<RobotsRefreshJob>();
        services.AddSingleton<IHtmlLinkExtractor, HtmlLinkExtractor>();

        // Both are singletons on purpose. A budget or a rate limit that each run observes on its
        // own bounds nothing about what several concurrent runs do together.
        services.AddSingleton<CrawlRequestBudget>();
        services.AddSingleton(provider => new HostRequestRateLimiter(
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<CrawlSchedulingOptions>().RequestsPerSecondPerHost));
        services.AddScoped<ICrawlRobotsReader, CrawlRobotsReader>();
        services.TryAddScoped<ICrawlResultSink, CrawlResultSink>();
        services.AddScoped<ICrawlReportReader, CrawlReportReader>();
        services.AddScoped<ICrawlExecutionService, CrawlExecutionService>();
        services.AddScoped<CrawlRunJob>();
        services.AddScoped<IPageAuditProvider, PageSpeedInsightsProvider>();
        services.AddScoped<PageAuditExecutionService>();
        services.AddScoped<PageAuditSchedulingService>();
        services.AddScoped<IPageAuditRunner>(provider =>
            provider.GetRequiredService<PageAuditSchedulingService>());
        services.AddScoped<IPageAuditReader, PageAuditReader>();
        services.AddScoped<PageAuditRunJob>();
        services.AddScoped<PageAuditDispatchJob>();
        services.AddScoped<IMaintenanceOccurrenceExpander, MaintenanceOccurrenceExpander>();
        services.AddScoped<MaintenanceExpansionJob>();
        services.AddScoped<IIncidentLifecycleService, IncidentLifecycleService>();
        services.AddScoped<IncidentVisibility>();
        services.AddScoped<IIncidentReader, IncidentReader>();
        services.AddScoped<INotificationFeedReader, NotificationFeedReader>();
        services.AddScoped<NotificationEventWriter>();
        services.AddScoped<IncidentAutomationService>();
        services.AddScoped<NotificationDispatchService>();
        services.AddScoped<NotificationReminderService>();
        services.TryAddSingleton<IEmailTransport, RecordingEmailTransport>();
        services.AddScoped<LogicalCheckJob>();
        services.AddScoped<MonitoringDispatchJob>();
        services.AddScoped<NotificationDispatchJob>();
        var hangfireEnabled = schedulingOptions.Enabled || notificationOptions.Enabled
            || maintenanceOptions.Enabled || seoOptions.Enabled || crawlOptions.Enabled
            || pageAuditOptions.Enabled;
        if (hangfireEnabled)
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
                var queues = new List<string>();
                if (schedulingOptions.Enabled)
                {
                    queues.Add(MonitoringQueueNames.ShortChecks);
                }

                if (notificationOptions.Enabled)
                {
                    queues.Add(NotificationQueueNames.Notifications);
                }

                if (maintenanceOptions.Enabled)
                {
                    queues.Add(MaintenanceQueueNames.Maintenance);
                }

                if (seoOptions.Enabled)
                {
                    queues.Add(SeoQueueNames.Seo);
                }

                options.Queues = queues.ToArray();
                options.WorkerCount = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
            });

            // A second server, serving only the crawl queue. Listing the crawl queue on the server
            // above would not isolate anything: Hangfire's queue order decides what a *free* worker
            // picks up next, it does not reserve workers, so long crawls would still starve the
            // monitoring queue. A separate pool is the only arrangement that reserves capacity.
            if (crawlOptions.Enabled)
            {
                services.AddHangfireServer(options =>
                {
                    options.ServerName = $"{Environment.MachineName}-crawl";
                    options.Queues = [CrawlQueueNames.Crawl];
                    options.WorkerCount = crawlOptions.WorkerCount;
                });
            }

            // A third server, serving only the page-audit queue, for the same reason the crawl
            // queue has its own: a PageSpeed call can take ninety seconds, and Hangfire's queue
            // order decides what a free worker picks up next rather than reserving any worker.
            // Only a separate pool keeps a long third-party call away from scheduled checks.
            if (pageAuditOptions.Enabled)
            {
                services.AddHangfireServer(options =>
                {
                    options.ServerName = $"{Environment.MachineName}-page-audits";
                    options.Queues = [PageAuditQueueNames.PageAudits];
                    options.WorkerCount = pageAuditOptions.WorkerCount;
                });
            }
        }

        if (schedulingOptions.Enabled)
        {
            services.AddScoped<ILogicalCheckQueue, HangfireLogicalCheckQueue>();
        }
        else
        {
            services.AddScoped<ILogicalCheckQueue, DisabledLogicalCheckQueue>();
        }

        if (pageAuditOptions.Enabled)
        {
            services.AddScoped<IPageAuditQueue, HangfirePageAuditQueue>();
        }
        else
        {
            services.AddScoped<IPageAuditQueue, DisabledPageAuditQueue>();
        }
        var configuredUserAgent = configuration[$"{SafeHttpTransportOptions.SectionName}:UserAgent"];
        var configuredContact = configuration[$"{SafeHttpTransportOptions.SectionName}:Contact"];
        var safeHttpOptions = new SafeHttpTransportOptions
        {
            UserAgent = string.IsNullOrWhiteSpace(configuredUserAgent)
                ? "WebHealthMonitor/1.0"
                : configuredUserAgent.Trim(),
            Contact = string.IsNullOrWhiteSpace(configuredContact) ? null : configuredContact.Trim()
        };
        ValidateContact(safeHttpOptions);
        services.AddSingleton(safeHttpOptions);
        ValidateCrawlOptions(crawlOptions, safeHttpOptions);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMonitoringDnsResolver, SystemMonitoringDnsResolver>();
        services.AddSingleton<IDestinationAddressPolicy, StrictDestinationAddressPolicy>();
        services.AddSingleton<SafeHttpConcurrencyLimiter>();
        services.AddScoped<IMonitoringTargetAuthorizer, MonitoringTargetAuthorizer>();
        services.AddScoped<ISafeHttpTransport, SafeHttpTransport>();
        services.AddScoped<ISslCertificateProbe, SslCertificateProbe>();
        services.AddScoped<ISslUrgentCheckScheduler, SslUrgentCheckScheduler>();
        services.AddHttpClient(SafeHttpTransportOptions.ClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(safeHttpOptions.UserAgentHeader);
            })
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
                SafeHttpConnectionFactory.Create(
                    serviceProvider.GetRequiredService<IMonitoringDnsResolver>(),
                    serviceProvider.GetRequiredService<IDestinationAddressPolicy>(),
                    serviceProvider.GetRequiredService<SafeHttpConcurrencyLimiter>(),
                    safeHttpOptions));

        // A dedicated client on a fixed Google origin. Deliberately not SafeHttpTransport: that
        // exists to contact user-configured targets under DNS and SSRF rules because the URL comes
        // from a user, and here the URL is one constant host with the monitored URL as a query
        // value. Redirects are off — this API does not redirect, and following one would be
        // following it with the API key still attached.
        services.AddHttpClient(PageSpeedInsightsOptions.ClientName, client =>
            {
                client.BaseAddress = new Uri(PageSpeedInsightsProvider.ServiceOrigin);

                // The provider applies its own timeout through a linked token, so it can tell a
                // timeout apart from a cancellation. A second one here would surface as an
                // ambiguous TaskCanceledException instead.
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(safeHttpOptions.UserAgentHeader);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All
            });

        services.AddHealthChecks()
            .AddCheck<PostgreSqlReadinessCheck>("postgresql", tags: ["ready"]);

        return services;
    }

    private static void ValidateSchedulingOptions(MonitoringSchedulingOptions options)
    {
        if (options.DispatchBatchSize is < 1 or > 500
            || options.RecoveryBatchSize is < 1 or > 1000
            || options.RecoveryDelay < TimeSpan.FromMinutes(1)
            || options.RecoveryDelay > TimeSpan.FromHours(1)
            || options.UrgentSslCooldown < TimeSpan.FromMinutes(5)
            || options.UrgentSslCooldown > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException("Monitoring scheduling options are outside their safe bounds.");
        }
    }

    /// <summary>
    /// The crawler's request budget is capped at half the transport's global concurrency. The cap
    /// is on <c>WorkerCount * RequestConcurrency</c>, not on <c>RequestConcurrency</c> alone: a
    /// worker pool of eight, each run staying within its own budget, would together fill every
    /// shared HTTP slot and block scheduled checks at the transport — which is exactly the
    /// starvation the separate queue exists to prevent, arriving by a different route.
    /// <para>
    /// <see cref="CrawlRequestBudget" /> enforces the same ceiling at runtime. This check exists so
    /// a configuration that could never respect it is refused at startup rather than silently
    /// queueing behind a semaphore.
    /// </para>
    /// </summary>
    private static void ValidateCrawlOptions(
        CrawlSchedulingOptions options,
        SafeHttpTransportOptions transportOptions)
    {
        if (options.WorkerCount is < 1 or > 8
            || options.RequestConcurrency < 1
            || options.WorkerCount * options.RequestConcurrency > transportOptions.GlobalConcurrency / 2
            || options.RequestsPerSecondPerHost is <= 0 or > 10
            || options.MaxDuration < TimeSpan.FromMinutes(1)
            || options.MaxDuration > TimeSpan.FromHours(4)
            || options.FetchTimeoutSeconds is < 1 or > 120
            || options.MaxPageBytes < 64 * 1024
            || options.MaxPageBytes > SafeHttpTransportDefaults.MaxDecodedBodyBytes)
        {
            throw new InvalidOperationException("Crawl scheduling options are outside their safe bounds.");
        }
    }

    /// <summary>
    /// BR-L09 asks for a contact identifier, so a configured one has to be reachable rather than
    /// decorative. It is optional; what is refused is a value that could not be acted on.
    /// </summary>
    private static void ValidateContact(SafeHttpTransportOptions options)
    {
        if (options.Contact is null) return;
        var contact = options.Contact;
        var usable = contact.Length <= 200
            && Uri.TryCreate(contact, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https" or "mailto";
        if (!usable)
        {
            throw new InvalidOperationException(
                $"{SafeHttpTransportOptions.SectionName}:Contact must be an absolute http, https or "
                + "mailto URI of at most 200 characters.");
        }
    }

    /// <summary>
    /// Refuses a configuration that could not work rather than letting it fail one run at a time.
    /// The API key is required only when scheduling is on: with the feature off the key is
    /// absent by design, and demanding one would make the disabled default unstartable.
    /// </summary>
    private static void ValidatePageAuditOptions(
        PageAuditSchedulingOptions options,
        PageSpeedInsightsOptions providerOptions)
    {
        if (options.WorkerCount is < 1 or > 4
            || options.DispatchBatchSize is < 1 or > 200
            || options.ReconciliationBatchSize is < 1 or > 500
            || options.ReconciliationDelay < TimeSpan.FromMinutes(1)
            || options.ReconciliationDelay > TimeSpan.FromHours(1)
            || options.DefaultInterval < TimeSpan.FromHours(6)
            || options.DefaultInterval > TimeSpan.FromDays(30)
            || options.MaximumAttempts is < 1 or > 5
            || options.LeaseDuration < providerOptions.RequestTimeout
            || options.LeaseDuration > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException(
                "Page audit scheduling options are outside their safe bounds.");
        }

        if (providerOptions.RequestTimeout < TimeSpan.FromSeconds(10)
            || providerOptions.RequestTimeout > TimeSpan.FromMinutes(5)
            || providerOptions.MaximumResponseBytes < 256 * 1024
            || providerOptions.MaximumResponseBytes > 64 * 1024 * 1024
            || providerOptions.MaximumAuditCount is < 1 or > 5000
            || string.IsNullOrWhiteSpace(providerOptions.Locale))
        {
            throw new InvalidOperationException(
                "PageSpeed Insights options are outside their safe bounds.");
        }

        if (options.Enabled && !providerOptions.HasApiKey)
        {
            throw new InvalidOperationException(
                "Page audit scheduling is enabled but no PageSpeed Insights API key is "
                + "configured. Set PageAudits__PageSpeedInsights__ApiKey.");
        }
    }

    private static void ValidateNotificationOptions(NotificationSchedulingOptions options)
    {
        if (options.DispatchBatchSize is < 1 or > 500
            || options.MaxAttempts is < 1 or > 20
            || options.InitialRetryDelay < TimeSpan.FromSeconds(30)
            || options.MaxRetryDelay < options.InitialRetryDelay
            || options.LeaseDuration < TimeSpan.FromMinutes(1)
            || options.ReminderInterval < TimeSpan.FromMinutes(5)
            || options.EscalationDelay < TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException("Notification scheduling options are outside their safe bounds.");
        }
    }

    private static void ValidateSeoOptions(SeoSchedulingOptions options)
    {
        if (options.RobotsTtlHours is < 1 or > 168
            || options.RefreshBatchSize is < 1 or > 500
            || options.FetchTimeoutSeconds is < 1 or > 120)
        {
            throw new InvalidOperationException("SEO scheduling options are outside their safe bounds.");
        }
    }

    private static void ValidateMaintenanceOptions(MaintenanceSchedulingOptions options)
    {
        if (options.HorizonDays is < 1 or > 730 || options.BatchSize is < 1 or > 500)
        {
            throw new InvalidOperationException("Maintenance scheduling options are outside their safe bounds.");
        }
    }

    private static void ValidateSmtpOptions(SmtpEmailOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Host)
            || options.Port is < 1 or > 65535
            || options.TimeoutSeconds is < 1 or > 120
            || string.IsNullOrWhiteSpace(options.FromAddress)
            || string.IsNullOrWhiteSpace(options.UserName)
            || string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException(
                "SMTP is enabled but its host, port, sender or credentials are missing or out of range.");
        }
    }
}
