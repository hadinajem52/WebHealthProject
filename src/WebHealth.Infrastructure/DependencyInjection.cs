using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebHealth.Infrastructure.Diagnostics;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Application.Administration;
using WebHealth.Application.Archiving;
using WebHealth.Infrastructure.Archiving;
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
using WebHealth.Application.PngAudits;
using WebHealth.Infrastructure.PngAudits;
using WebHealth.Application.SiteAnalysis;
using WebHealth.Infrastructure.SiteAnalysis;
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

        var retentionOptions = configuration.GetSection(MonitoringRetentionOptions.SectionName)
            .Get<MonitoringRetentionOptions>() ?? new MonitoringRetentionOptions();
        retentionOptions.Validate();
        services.AddSingleton(retentionOptions);

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

        var pngAuditOptions = configuration.GetSection(PngAuditOptions.SectionName)
            .Get<PngAuditOptions>() ?? new PngAuditOptions();
        ValidatePngAuditOptions(pngAuditOptions);
        services.AddSingleton(pngAuditOptions);
        services.AddSingleton(new PngRecommendationThresholds(
            pngAuditOptions.MinSavingsPercent,
            pngAuditOptions.MinSavingsBytes));
        services.AddSingleton(new PngImageAnalysisLimits(
            pngAuditOptions.MaxImageBytes,
            pngAuditOptions.MaxWidth,
            pngAuditOptions.MaxHeight,
            pngAuditOptions.MaxDecodedPixels,
            pngAuditOptions.MaxDecodedMemoryBytes));

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
            services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
        }

        services.AddDbContextFactory<ApplicationDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString(DatabaseConnectionName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{DatabaseConnectionName} is not configured.");
            }

            PostgreSqlDbContextOptions.Configure(options, connectionString);
        });
        services.AddScoped(provider =>
            provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

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
        services.AddScoped<RegistryHierarchyLock>();
        services.AddScoped<RegistryArchiveCascade>();
        services.AddScoped<EndpointPurgeCascade>();
        services.AddScoped<WebsitePurgeCascade>();
        services.AddScoped<ClientPurgeCascade>();
        services.AddScoped<IRegistryReader, RegistryReader>();
        services.AddScoped<ClientRegistryService>();
        services.AddScoped<IClientRegistryService>(provider =>
            provider.GetRequiredService<ClientRegistryService>());
        services.AddScoped<WebsiteRegistryService>();
        services.AddScoped<IWebsiteRegistryService>(provider =>
            provider.GetRequiredService<WebsiteRegistryService>());
        services.AddScoped<ITargetRegistryReader, TargetRegistryReader>();
        services.AddScoped<EnvironmentRegistryService>();
        services.AddScoped<IEnvironmentRegistryService>(provider =>
            provider.GetRequiredService<EnvironmentRegistryService>());
        services.AddScoped<EndpointRegistryService>();
        services.AddScoped<IEndpointRegistryService>(provider =>
            provider.GetRequiredService<EndpointRegistryService>());
        services.AddScoped<IEndpointRegistrationService, EndpointRegistrationService>();
        services.AddScoped<IEndpointTestGate, EndpointTestGate>();
        services.AddScoped<IMonitoringEligibilityService, MonitoringEligibilityService>();
        services.AddScoped<IExecutionLeaseService, ExecutionLeaseService>();
        services.AddScoped<ILogicalCheckFinalizationService, LogicalCheckFinalizationService>();
        services.AddScoped<ILogicalCheckExecutionService, LogicalCheckExecutionService>();
        services.AddScoped<IMonitoringSchedulingService, MonitoringSchedulingService>();
        services.AddScoped<MonitoringRuntimeRecorder>();
        services.AddScoped<IRetentionHoldService, RetentionHoldService>();
        services.AddScoped<DailyAggregateWriter>();
        services.AddScoped<ExecutionAttemptRetentionBatch>();
        services.AddScoped<IMonitoringWorkerReader>(provider => schedulingOptions.Enabled
            ? new HangfireMonitoringWorkerReader(provider.GetRequiredService<JobStorage>(),
                provider.GetRequiredService<ILogger<HangfireMonitoringWorkerReader>>())
            : new DisabledMonitoringWorkerReader());
        services.AddScoped<IManualCheckService, ManualCheckService>();
        services.AddScoped<ICheckHistoryReader, CheckHistoryReader>();
        services.AddScoped<IRunHistoryArchive, RunHistoryArchive>();
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
        services.AddSingleton<IHtmlDocumentDiscoveryExtractor, HtmlDocumentDiscoveryExtractor>();
        services.AddSingleton<IHtmlLinkExtractor, HtmlLinkExtractor>();
        services.AddSingleton<SiteAnalysisRequestBudget>();
        services.AddSingleton<SiteAnalysisHostRateLimiter>();
        services.AddScoped<ISiteAnalysisFetcher, SiteAnalysisFetcher>();
        services.AddScoped<ICrawlRobotsReader, CrawlRobotsReader>();
        services.AddScoped<IPngSiteCrawler, PngSiteCrawler>();
        services.AddSingleton<IPngFormatComparisonEngine, MagickPngFormatComparisonEngine>();
        services.AddSingleton<IPngImageAnalyzer, PngImageAnalyzer>();
        services.TryAddScoped<IPngAuditResultSink, PngAuditResultSink>();
        services.AddScoped<IPngAuditReader, PngAuditReader>();
        services.AddScoped<IPngAuditReconciler, PngAuditReconciler>();
        services.AddScoped<IPngAuditRunner, PngAuditRunner>();
        services.AddScoped<PngAuditQueuedRunReader>();
        services.AddScoped<PngAuditExecutionService>();
        services.AddScoped<PngAuditRunJob>();
        services.AddScoped<PngAuditReconciliationJob>();
        services.TryAddScoped<ICrawlResultSink, CrawlResultSink>();
        services.AddScoped<ICrawlReportReader, CrawlReportReader>();
        services.AddScoped<ICrawlReconciler, CrawlReconciler>();
        services.AddScoped<ICrawlExecutionService, CrawlExecutionService>();
        services.AddScoped<CrawlQueuedRunReader>();
        services.AddScoped<CrawlRunJob>();
        services.AddScoped<CrawlReconciliationJob>();
        if (crawlOptions.Enabled)
        {
            services.AddScoped<ICrawlRunQueue, HangfireCrawlRunQueue>();
        }

        services.AddScoped<ICrawlRunner, CrawlRunner>();
        services.AddScoped<IPageAuditProvider, PageSpeedInsightsProvider>();
        services.AddScoped<PageAuditExecutionService>();
        services.AddScoped<PageAuditSchedulingService>();
        services.AddScoped<IPageAuditRunner>(provider =>
            provider.GetRequiredService<PageAuditSchedulingService>());
        services.AddScoped<IPageAuditReader, PageAuditReader>();
        services.AddScoped<IPageAuditIncidentPolicyService, PageAuditIncidentPolicyService>();
        services.AddScoped<IPageAuditIncidentAutomationService, PageAuditIncidentAutomationService>();
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
            || pageAuditOptions.Enabled || pngAuditOptions.Enabled;
        if (hangfireEnabled)
        {
            var connectionString = configuration.GetConnectionString(DatabaseConnectionName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{DatabaseConnectionName} is not configured.");
            }

            var queuePollInterval = configuration.GetValue<TimeSpan?>(
                "Hangfire:QueuePollInterval") ?? TimeSpan.FromSeconds(5);
            if (queuePollInterval < TimeSpan.FromSeconds(1)
                || queuePollInterval > TimeSpan.FromMinutes(5))
            {
                throw new InvalidOperationException(
                    "Hangfire:QueuePollInterval must be between one second and five minutes.");
            }

            services.AddHangfire(configurationBuilder => configurationBuilder
                .UsePostgreSqlStorage(bootstrapper => bootstrapper.UseNpgsqlConnection(connectionString),
                    new PostgreSqlStorageOptions
                    {
                        SchemaName = "hangfire",
                        PrepareSchemaIfNecessary = false,
                        QueuePollInterval = queuePollInterval
                    }));
            var sharedQueues = new List<string>();
            if (schedulingOptions.Enabled)
            {
                sharedQueues.Add(MonitoringQueueNames.ShortChecks);
            }

            if (notificationOptions.Enabled)
            {
                sharedQueues.Add(NotificationQueueNames.Notifications);
            }

            if (maintenanceOptions.Enabled)
            {
                sharedQueues.Add(MaintenanceQueueNames.Maintenance);
            }

            if (seoOptions.Enabled)
            {
                sharedQueues.Add(SeoQueueNames.Seo);
            }

            if (sharedQueues.Count > 0)
            {
                services.AddHangfireServer(options =>
                {
                    options.Queues = [.. sharedQueues];
                    options.WorkerCount = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
                });
            }

            if (crawlOptions.Enabled)
            {
                services.AddHangfireServer(options =>
                {
                    options.ServerName = $"{Environment.MachineName}-crawl";
                    options.Queues = [CrawlQueueNames.Crawl];
                    options.WorkerCount = crawlOptions.WorkerCount;
                });
            }

            if (pageAuditOptions.Enabled)
            {
                services.AddHangfireServer(options =>
                {
                    options.ServerName = $"{Environment.MachineName}-page-audits";
                    options.Queues = [PageAuditQueueNames.PageAudits];
                    options.WorkerCount = pageAuditOptions.WorkerCount;
                });
            }

            if (pngAuditOptions.Enabled)
            {
                services.AddHangfireServer(options =>
                {
                    options.ServerName = $"{Environment.MachineName}-image-audits";
                    options.Queues = [PngAuditQueueNames.ImageAudits];
                    options.WorkerCount = pngAuditOptions.WorkerCount;
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

        if (pngAuditOptions.Enabled)
        {
            services.AddScoped<IPngAuditRunQueue, HangfirePngAuditRunQueue>();
        }
        else
        {
            services.AddScoped<IPngAuditRunQueue, DisabledPngAuditRunQueue>();
        }
        var configuredUserAgent = configuration[$"{SafeHttpTransportOptions.SectionName}:UserAgent"];
        var configuredContact = configuration[$"{SafeHttpTransportOptions.SectionName}:Contact"];
        var safeHttpOptions = new SafeHttpTransportOptions
        {
            UserAgent = string.IsNullOrWhiteSpace(configuredUserAgent)
                ? "WebHealthMonitor/1.0"
                : configuredUserAgent.Trim(),
            Contact = string.IsNullOrWhiteSpace(configuredContact) ? null : configuredContact.Trim(),
            PerAddressConnectTimeout = configuration.GetValue<TimeSpan?>(
                $"{SafeHttpTransportOptions.SectionName}:PerAddressConnectTimeout") ?? TimeSpan.FromSeconds(5)
        };
        if (safeHttpOptions.PerAddressConnectTimeout < TimeSpan.FromSeconds(1)
            || safeHttpOptions.PerAddressConnectTimeout > TimeSpan.FromSeconds(10))
        {
            throw new InvalidOperationException("Monitoring:HttpTransport:PerAddressConnectTimeout must be between 1 and 10 seconds.");
        }
        ValidateContact(safeHttpOptions);
        services.AddSingleton(safeHttpOptions);
        ValidateCrawlOptions(crawlOptions, safeHttpOptions);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMonitoringDnsResolver, SystemMonitoringDnsResolver>();
        services.AddSingleton<IDestinationAddressPolicy, StrictDestinationAddressPolicy>();
        services.AddSingleton<SafeHttpConcurrencyLimiter>();
        services.AddSingleton<PngImageRequestGate>();
        services.AddScoped<SafeHttpTransport>();
        services.AddScoped<ISafeHttpTransport>(provider =>
            provider.GetRequiredService<SafeHttpTransport>());
        services.AddScoped<IPngImageTransport, PngImageTransport>();
        services.AddScoped<IEndpointUrlSchemeProbe, EndpointUrlSchemeProbe>();
        services.AddScoped<EndpointUrlResolver>();
        services.AddScoped<ISslCertificateProbe, SslCertificateProbe>();
        services.AddScoped<ITargetConnectionAuthorization, TargetConnectionAuthorization>();
        services.AddScoped<ITargetPermissionService, TargetPermissionService>();
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

        services.AddHttpClient(PageSpeedInsightsOptions.ClientName, client =>
            {
                client.BaseAddress = new Uri(PageSpeedInsightsProvider.ServiceOrigin);

                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(safeHttpOptions.UserAgentHeader);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All
            })

            .RemoveAllLoggers();

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
            || options.DispatchDelayGrace < TimeSpan.FromMinutes(2)
            || options.DispatchDelayGrace > TimeSpan.FromMinutes(30)
            || options.UrgentSslCooldown < TimeSpan.FromMinutes(5)
            || options.UrgentSslCooldown > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException("Monitoring scheduling options are outside their safe bounds.");
        }
    }

    private static void ValidateCrawlOptions(
        CrawlSchedulingOptions options,
        SafeHttpTransportOptions transportOptions)
    {
        if (options.WorkerCount is < 1 or > 8
            || options.RequestConcurrency < 1
            || options.WorkerCount * options.RequestConcurrency > transportOptions.GlobalConcurrency / 2
            || !double.IsFinite(options.RequestsPerSecondPerHost)
            || options.RequestsPerSecondPerHost is <= 0 or > 10
            || options.MaxDuration < TimeSpan.FromMinutes(1)
            || options.MaxDuration > TimeSpan.FromHours(4)
            || options.FetchTimeoutSeconds is < 1 or > 120
            || options.TransientRetryCount is < 0 or > 3
            || options.RetryBaseDelay < TimeSpan.Zero
            || options.RetryBaseDelay > TimeSpan.FromSeconds(5)
            || options.MaxRetryDelay < options.RetryBaseDelay
            || options.MaxRetryDelay > TimeSpan.FromMinutes(2)
            || options.MaxPageBytes < 64 * 1024
            || options.MaxPageBytes > SafeHttpTransportDefaults.DefaultMaxResponseBodyBytes)
        {
            throw new InvalidOperationException("Crawl scheduling options are outside their safe bounds.");
        }
    }

    private static void ValidatePngAuditOptions(PngAuditOptions options)
    {
        if (options.WorkerCount != 1
            || options.MaxPages is < 1 or > 1000
            || options.MaxDepth is < 0 or > 10
            || options.MaxPageBytes < 1
            || options.MaxPageBytes > SafeHttpTransportDefaults.DefaultMaxResponseBodyBytes
            || options.MaxTotalPageBytes < options.MaxPageBytes
            || options.MaxTotalPageBytes > 512L * 1024 * 1024
            || options.MaxImageReferencesPerPage is < 1
                or > HtmlDocumentDiscoveryLimits.MaxImageReferences
            || options.MaxUniqueImages is < 1 or > 5000
            || options.MaxTotalImageSourceMappings < options.MaxUniqueImages
            || options.MaxTotalImageSourceMappings > 50000
            || options.MaxImageBytes < 1
            || options.MaxImageBytes > SafeHttpTransportDefaults.AbsoluteMaxResponseBodyBytes
            || options.MaxTotalImageBytes < options.MaxImageBytes
            || options.MaxTotalImageBytes > 1024L * 1024 * 1024
            || options.MaxWidth is < 1 or > 100000
            || options.MaxHeight is < 1 or > 100000
            || options.MaxDecodedPixels is < 1 or > 1000000000
            || options.MaxDecodedMemoryBytes < options.MaxDecodedPixels * 4
            || options.MaxDecodedMemoryBytes > 4L * 1024 * 1024 * 1024
            || options.MaxTotalHttpAttempts is < 1 or > 100000
            || options.FetchTimeoutSeconds is < 1 or > SafeHttpTransportDefaults.MaxTimeoutSeconds
            || !double.IsFinite(options.RequestsPerSecondPerHost)
            || options.RequestsPerSecondPerHost is <= 0 or > 10
            || options.TransientRetryCount is < 0 or > 3
            || options.ImageFetchConcurrency != 1
            || options.ImageDecodeConcurrency != 1
            || options.ComparisonTimeoutSeconds is < 1 or > 300
            || options.MaximumAttempts is < 1 or > 5
            || options.LeaseDuration < TimeSpan.FromSeconds(30)
            || options.LeaseDuration > TimeSpan.FromMinutes(15)
            || options.HeartbeatInterval < TimeSpan.FromSeconds(5)
            || options.HeartbeatInterval >= options.LeaseDuration
            || options.ReconciliationDelay < TimeSpan.FromMinutes(1)
            || options.ReconciliationDelay > TimeSpan.FromHours(1)
            || options.ReconciliationBatchSize is < 1 or > 500
            || options.MaxDuration < TimeSpan.FromMinutes(1)
            || options.MaxDuration > TimeSpan.FromHours(4)
            || options.MinSavingsPercent is < 0 or > 100
            || options.MinSavingsBytes < 0
            || options.MinSavingsBytes > options.MaxImageBytes)
        {
            throw new InvalidOperationException("PNG audit options are outside their safe bounds.");
        }
    }

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

    private static void ValidatePageAuditOptions(
        PageAuditSchedulingOptions options,
        PageSpeedInsightsOptions providerOptions)
    {
        if (options.WorkerCount is < 1 or > 4
            || options.DispatchBatchSize is < 1 or > 200
            || options.ReconciliationBatchSize is < 1 or > 500
            || options.ReconciliationDelay < TimeSpan.FromMinutes(1)
            || options.ReconciliationDelay > TimeSpan.FromHours(1)

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
