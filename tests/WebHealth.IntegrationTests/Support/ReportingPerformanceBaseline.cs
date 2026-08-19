using System.Diagnostics;
using System.Globalization;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Identity;
using WebHealth.Application.Administration;
using WebHealth.Infrastructure.Registry;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.IntegrationTests.Support;

/// <summary>
/// Phase 5 increment 5.7: representative data, query plans, and the NFR-02 dashboard baseline.
/// </summary>
/// <remarks>
/// <para>
/// Plans are captured with <c>auto_explain</c> rather than by running <c>EXPLAIN</c> over SQL
/// copied into this file. The reporting layer issues a mixture of Entity Framework translations
/// and raw aggregates, and a transcription of either would be evidence about the transcription
/// rather than about the application: it could drift the moment the reader changed and nothing
/// would fail. With <c>auto_explain</c> the plan recorded is the plan PostgreSQL chose for the
/// statement the application actually sent.
/// </para>
/// <para>
/// Plan capture and timing are separate passes. <c>auto_explain</c> with <c>log_analyze</c>
/// instruments every node, which inflates the very durations NFR-02 is about, so the timing pass
/// runs with it switched off.
/// </para>
/// </remarks>
internal static class ReportingPerformanceBaseline
{
    // The fleet. Chosen to sit above what this project's own deployment would carry, so the
    // headroom is measured rather than assumed, while staying inside the 5,000-monitor bound the
    // query layer refuses to exceed.
    private const int ClientCount = 6;
    private const int WebsitesPerClient = 4;
    private const int EndpointsPerEnvironment = 2;
    private const int HistoryDays = 90;

    /// <summary>
    /// Iterations per scenario.
    /// </summary>
    /// <remarks>
    /// Ten, which keeps a whole run inside about ten minutes on a developer machine. The
    /// percentile index is <c>ceil(0.95 x n) - 1</c>, so at ten samples the reported P95 is the
    /// slowest of the ten rather than an interpolated value. That reads high rather than low,
    /// which is the right direction for a budget check: it cannot flatter a result into passing.
    /// </remarks>
    private const int TimedIterations = 10;

    /// <summary>NFR-02: the dashboard answers within three seconds.</summary>
    private static readonly TimeSpan DashboardBudget = TimeSpan.FromSeconds(3);

    private static readonly DateTimeOffset AsOf = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

    public static async Task VerifyAsync(
        string connectionString,
        string serverLogPath,
        string evidencePath)
    {
        await using var services = BuildServices(connectionString);
        var fixture = await SeedAsync(services, connectionString);

        var scenarios = BuildScenarios(fixture);
        var plans = await CapturePlansAsync(services, connectionString, serverLogPath, scenarios);
        var timings = await MeasureAsync(services, scenarios);

        await File.WriteAllTextAsync(
            evidencePath,
            RenderEvidence(fixture, scenarios, plans, timings),
            new UTF8Encoding(false));

        // NFR-02 is stated about the dashboard, so it is the dashboard scenarios that carry the
        // budget; the export and the trend endpoint are recorded but not gated by it.
        foreach (var scenario in scenarios.Where(candidate => candidate.IsDashboard))
        {
            timings[scenario.Name].Percentile95.Should().BeLessThan(
                DashboardBudget,
                "NFR-02 requires the dashboard to answer within three seconds ({0})",
                scenario.Name);
        }
    }

    /// <summary>
    /// The measured services, connected the way the application connects.
    /// </summary>
    /// <remarks>
    /// Pooling is forced on. The harness scripts disable it in their own connection string so a
    /// database can be dropped and recreated between steps, but measuring through an unpooled
    /// string would time a fresh TCP connection and authentication handshake for every statement
    /// a screen issues - about two and a half seconds per dashboard here, none of which the
    /// application pays. That would be a measurement of the harness.
    /// </remarks>
    private static ServiceProvider BuildServices(string connectionString)
    {
        var pooled = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = true }.ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = pooled,
            ["BootstrapAdmin:Email"] = "bootstrap@example.test",
            ["BootstrapAdmin:DisplayName"] = "Bootstrap Administrator",
            ["BootstrapAdmin:Password"] = $"Baseline-9!{Guid.NewGuid():N}"
        }).Build();

        return new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
    }

    // ------------------------------------------------------------------ seeding

    /// <summary>
    /// The registry is built through the real services so the rows carry real fingerprints,
    /// snapshots and monitor defaults; the history is written with set-based SQL because two
    /// million samples through the change tracker would measure Entity Framework rather than
    /// PostgreSQL.
    /// </summary>
    private static async Task<BaselineFixture> SeedAsync(
        ServiceProvider services,
        string connectionString)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().BootstrapAsync();

        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientService = scope.ServiceProvider.GetRequiredService<IClientRegistryService>();
        var websiteService = scope.ServiceProvider.GetRequiredService<IWebsiteRegistryService>();
        var environmentService = scope.ServiceProvider.GetRequiredService<IEnvironmentRegistryService>();
        var endpointService = scope.ServiceProvider.GetRequiredService<IEndpointRegistryService>();

        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var ownerSubjectId = await database.OwnerSubjects
            .Where(owner => owner.UserId == administrator.Id)
            .Select(owner => owner.Id)
            .SingleAsync();
        var access = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);

        var firstClientId = Guid.Empty;
        for (var clientIndex = 0; clientIndex < ClientCount; clientIndex++)
        {
            var client = await clientService.CreateAsync(
                new($"Baseline Client {clientIndex:D2}", ownerSubjectId, null), access);
            Succeeded(client, "client");
            if (clientIndex == 0)
            {
                firstClientId = client.EntityId!.Value;
            }

            for (var websiteIndex = 0; websiteIndex < WebsitesPerClient; websiteIndex++)
            {
                var websiteName = $"Site {clientIndex:D2}-{websiteIndex:D2}";
                // A website cannot be enabled before it has an active environment, so it is
                // created disabled and enabled once its environments exist.
                var website = await websiteService.CreateAsync(
                    new(client.EntityId!.Value, websiteName, ownerSubjectId, null, false, []), access);
                Succeeded(website, "website");

                // One production and one staging environment, so the fleet carries both default
                // cadences: five minutes and fifteen.
                foreach (var environmentType in new[] { EnvironmentTypes.Production, EnvironmentTypes.Staging })
                {
                    var environment = await environmentService.CreateAsync(
                        new(website.EntityId!.Value, environmentType, environmentType, null, true), access);
                    Succeeded(environment, "environment");

                    for (var endpointIndex = 0; endpointIndex < EndpointsPerEnvironment; endpointIndex++)
                    {
                        var host = $"c{clientIndex:D2}-s{websiteIndex:D2}-"
                            + $"{environmentType.ToLowerInvariant()}-{endpointIndex:D2}.baseline.test";
                        var endpoint = await endpointService.CreateAsync(
                            new(environment.EntityId!.Value, $"https://{host}/status", null, true, null,
                                TargetAuthorizationKinds.Owned, "Baseline fixture owned by the project.", null),
                            access);
                        Succeeded(endpoint, "endpoint");
                    }
                }

                var enabled = await websiteService.UpdateAsync(
                    new(website.EntityId!.Value, websiteName, ownerSubjectId, null, true, 1, []), access);
                Succeeded(enabled, "website enable");
            }
        }

        var viewerId = await CreateViewerWithOneClientGrantAsync(
            scope, database, administrator.Id, firstClientId);
        await WriteHistoryAsync(connectionString);

        var monitorCount = await database.EndpointMonitors.CountAsync(monitor => monitor.DeletedAt == null);
        var sampleCount = await database.CheckResults.CountAsync();

        return new(
            new(administrator.Id, [ApplicationRoles.Administrator]),
            new(viewerId, [ApplicationRoles.Viewer]),
            firstClientId,
            monitorCount,
            sampleCount);
    }

    /// <summary>Fails with the service's own errors rather than with a bare false.</summary>
    private static void Succeeded(RegistryMutationResult result, string what) =>
        result.Succeeded.Should().BeTrue(
            "creating the baseline {0} must succeed, but it reported {1}: {2}",
            what,
            result.Status,
            string.Join("; ", result.Errors));

    /// <summary>
    /// A viewer scoped to one client. The administrator's plans never exercise the visibility
    /// scope at all - <c>ApplyEndpointScope</c> short-circuits for global access - so measuring
    /// only the administrator would leave the grant subqueries every other role pays for on the
    /// critical path entirely unmeasured.
    /// </summary>
    private static async Task<Guid> CreateViewerWithOneClientGrantAsync(
        AsyncServiceScope scope,
        ApplicationDbContext database,
        Guid administratorId,
        Guid clientId)
    {
        var administration = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
        var created = await administration.CreateUserAsync(
            new("Baseline Viewer", "baseline-viewer@example.test",
                $"Baseline-Viewer-4!{Guid.NewGuid():N}", [ApplicationRoles.Viewer]),
            administratorId);
        created.Succeeded.Should().BeTrue();

        database.AccessGrants.Add(new AccessGrant
        {
            Id = Guid.NewGuid(),
            UserId = created.UserId!.Value,
            AccessLevel = "Read",
            ClientId = clientId,
            EffectiveFrom = AsOf.AddDays(-HistoryDays * 2),
            CreatedAt = AsOf.AddDays(-HistoryDays * 2),
            CreatedByUserId = administratorId
        });
        await database.SaveChangesAsync();

        return created.UserId!.Value;
    }

    private static async Task WriteHistoryAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Two of the tables written below carry DEFERRABLE per-row constraint triggers, which
        // queue one event per inserted row and settle them all at commit. That is the right
        // design for the application, which inserts a handful of rows per check, and completely
        // impractical for a bulk load of millions. The load therefore runs with replication role
        // 'replica', which is PostgreSQL's own answer for this, and every invariant those
        // triggers and the foreign keys enforce is re-checked as a set operation once the role is
        // back to normal - see VerifyHistoryIntegrityAsync. Skipping the checks without
        // re-running them would mean measuring queries over data that might not be valid.
        await ExecuteAsync(connection, "SET session_replication_role = replica;");
        try
        {
            await using var transaction = await connection.BeginTransactionAsync();
            foreach (var statement in HistoryStatements)
            {
                await using var command = new NpgsqlCommand(statement, connection, transaction);
                command.CommandTimeout = 0;
                command.Parameters.AddWithValue("history_start", AsOf.AddDays(-HistoryDays));
                command.Parameters.AddWithValue("history_end", AsOf);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        finally
        {
            // Only if the connection survived. Restoring the role on a connection the server has
            // already dropped throws, and that secondary failure replaces the one that actually
            // explains the run - which is exactly how a cluster being recreated underneath this
            // harness surfaced as a bare "Connection is not open".
            if (connection.State == System.Data.ConnectionState.Open)
            {
                await ExecuteAsync(connection, "SET session_replication_role = origin;");
            }
        }

        await VerifyHistoryIntegrityAsync(connection);

        // Without statistics, every plan below would be a plan for an empty table.
        await ExecuteAsync(connection, "ANALYZE;");
    }

    /// <summary>
    /// Re-establishes, as set operations, what the bypassed row triggers and foreign keys would
    /// have enforced one row at a time.
    /// </summary>
    private static async Task VerifyHistoryIntegrityAsync(NpgsqlConnection connection)
    {
        var invariants = new (string Description, string Sql)[]
        {
            ("every non-pending check carries its configuration snapshot",
                """
                SELECT count(*) FROM web_health.logical_check AS check_row
                LEFT JOIN web_health.check_configuration_snapshot AS snapshot
                  ON snapshot.logical_check_id = check_row.id
                WHERE check_row.state <> 'Pending' AND snapshot.logical_check_id IS NULL;
                """),
            ("every check belongs to a live monitor",
                """
                SELECT count(*) FROM web_health.logical_check AS check_row
                LEFT JOIN web_health.endpoint_monitor AS monitor
                  ON monitor.id = check_row.endpoint_monitor_id
                WHERE monitor.id IS NULL;
                """),
            ("every result names the monitor of the check it belongs to",
                """
                SELECT count(*) FROM web_health.check_result AS result
                LEFT JOIN web_health.logical_check AS check_row
                  ON check_row.id = result.logical_check_id
                 AND check_row.endpoint_monitor_id = result.endpoint_monitor_id
                WHERE check_row.id IS NULL;
                """),
            ("no result claims a maintenance occurrence",
                "SELECT count(*) FROM web_health.check_result WHERE maintenance_occurrence_id IS NOT NULL;"),
            ("every certificate observation matches its check's monitor",
                """
                SELECT count(*) FROM web_health.certificate_observation AS observation
                LEFT JOIN web_health.logical_check AS check_row
                  ON check_row.id = observation.logical_check_id
                 AND check_row.endpoint_monitor_id = observation.endpoint_monitor_id
                WHERE check_row.id IS NULL;
                """)
        };

        foreach (var (description, sql) in invariants)
        {
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
            var offenders = (long)(await command.ExecuteScalarAsync())!;
            offenders.Should().Be(0, "the seeded history must satisfy: {0}", description);
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// One transaction, because the deferred trigger on <c>logical_check</c> requires every
    /// non-pending check to carry its configuration snapshot by commit time.
    /// </summary>
    private static IReadOnlyList<string> HistoryStatements { get; } =
    [
        // A slot per monitor per cadence interval. The identifier is derived from the monitor and
        // the instant, so re-running the harness produces byte-identical rows.
        """
        INSERT INTO web_health.logical_check
            (id, endpoint_monitor_id, source, scheduled_for, state, cadence_key,
             policy_fingerprint, created_at, queued_at, started_at, completed_at)
        SELECT
            md5(monitor.id::text || '|' || slot::text)::uuid,
            monitor.id,
            'Scheduled',
            slot,
            'Completed',
            to_char(slot AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
            monitor.configuration_fingerprint,
            slot, slot, slot, slot
        FROM web_health.endpoint_monitor AS monitor
        CROSS JOIN LATERAL generate_series(
            @history_start,
            @history_end - interval '1 microsecond',
            make_interval(secs => monitor.interval_seconds)) AS slot
        WHERE monitor.deleted_at IS NULL;
        """,

        """
        INSERT INTO web_health.check_configuration_snapshot
            (logical_check_id, schema_version, monitor_type, configuration_fingerprint,
             interval_seconds, timeout_seconds, failure_confirmation_count,
             recovery_confirmation_count, warning_threshold_ms, critical_threshold_ms,
             interval_source, timeout_source, confirmation_source, threshold_source, created_at)
        SELECT
            check_row.id, 1, monitor.monitor_type, monitor.configuration_fingerprint,
            monitor.interval_seconds, monitor.timeout_seconds, monitor.failure_confirmation_count,
            monitor.recovery_confirmation_count, monitor.warning_threshold_ms,
            monitor.critical_threshold_ms,
            'EnvironmentDefault', 'PolicyProfile', 'PolicyProfile', 'PolicyProfile',
            check_row.created_at
        FROM web_health.logical_check AS check_row
        JOIN web_health.endpoint_monitor AS monitor ON monitor.id = check_row.endpoint_monitor_id;
        """,

        // Outcomes and durations are derived from a hash of the check identifier: reproducible,
        // uncorrelated with time, and spread widely enough that the percentile aggregates have a
        // real distribution to sort rather than one repeated value.
        """
        INSERT INTO web_health.check_result
            (logical_check_id, endpoint_monitor_id, outcome, failure_category, http_status,
             total_duration_ms, response_truncated, monitor_source, measured_at,
             counts_for_uptime, completed_at, is_maintenance)
        SELECT
            check_row.id,
            check_row.endpoint_monitor_id,
            sample.outcome,
            CASE sample.outcome
                WHEN 'Healthy' THEN NULL
                WHEN 'Warning' THEN 'SlowResponse'
                ELSE 'Timeout'
            END,
            CASE WHEN sample.outcome = 'Critical' THEN NULL ELSE 200 END,
            CASE sample.outcome
                WHEN 'Healthy' THEN 90 + (sample.bucket % 300)
                WHEN 'Warning' THEN 1600 + (sample.bucket % 900)
                ELSE 30000
            END,
            false,
            CASE WHEN monitor.monitor_type = 'SslCertificate'
                 THEN 'WebHealthSslProbeV1' ELSE 'WebHealthSafeHttpV1' END,
            check_row.scheduled_for,
            -- A certificate check is never an availability sample (BR-U03), and roughly one
            -- percent of the rest stand in for maintenance-suppressed runs (BR-U02).
            monitor.monitor_type <> 'SslCertificate' AND sample.bucket % 97 <> 0,
            check_row.scheduled_for,
            false
        FROM web_health.logical_check AS check_row
        JOIN web_health.endpoint_monitor AS monitor ON monitor.id = check_row.endpoint_monitor_id
        CROSS JOIN LATERAL (
            SELECT
                bucket,
                CASE
                    WHEN bucket % 1024 < 8 THEN 'Critical'
                    WHEN bucket % 1024 < 28 THEN 'Warning'
                    ELSE 'Healthy'
                END AS outcome
            FROM (SELECT abs(hashtextextended(check_row.id::text, 0)) AS bucket) AS hashed
        ) AS sample;
        """,

        // One observation per certificate check, with expiry spread across every band so the
        // certificate card has to classify rather than count one repeated value.
        """
        INSERT INTO web_health.certificate_observation
            (logical_check_id, endpoint_monitor_id, subject, issuer, serial_number,
             sha256_fingerprint, not_before, not_after, days_remaining, validation_category,
             hostname_matched, chain_trusted, subject_alternative_names, observed_at)
        SELECT
            check_row.id,
            check_row.endpoint_monitor_id,
            'CN=' || endpoint.normalized_host,
            'CN=WebHealth Baseline Issuer',
            lpad(to_hex(abs(hashtextextended(endpoint.id::text, 3))), 32, '0'),
            md5(endpoint.id::text) || md5(endpoint.id::text || 'fingerprint'),
            check_row.scheduled_for - interval '90 days',
            check_row.scheduled_for + make_interval(days => band.days_remaining),
            band.days_remaining,
            CASE
                WHEN band.days_remaining < 0 THEN 'Expired'
                WHEN abs(hashtextextended(endpoint.id::text, 5)) % 40 = 0 THEN 'Untrusted'
                ELSE 'Valid'
            END,
            true, true, NULL,
            check_row.scheduled_for
        FROM web_health.logical_check AS check_row
        JOIN web_health.endpoint_monitor AS monitor ON monitor.id = check_row.endpoint_monitor_id
        JOIN web_health.endpoint AS endpoint ON endpoint.id = monitor.endpoint_id
        CROSS JOIN LATERAL (
            SELECT (abs(hashtextextended(endpoint.id::text, 11)) % 400)::int - 5 AS days_remaining
        ) AS band
        WHERE monitor.monitor_type = 'SslCertificate';
        """,

        // BR-U06: the dashboard reads the latest confirmed state, so every monitor has one.
        """
        INSERT INTO web_health.endpoint_health
            (endpoint_monitor_id, evidence_logical_check_id, confirmed_status, confirmed_at, version)
        SELECT DISTINCT ON (check_row.endpoint_monitor_id)
            check_row.endpoint_monitor_id,
            check_row.id,
            CASE result.outcome
                WHEN 'Healthy' THEN 'Healthy'
                WHEN 'Warning' THEN 'Warning'
                ELSE 'Critical'
            END,
            result.measured_at,
            1
        FROM web_health.logical_check AS check_row
        JOIN web_health.check_result AS result ON result.logical_check_id = check_row.id
        ORDER BY check_row.endpoint_monitor_id, result.measured_at DESC, check_row.id;
        """,

        // An open incident wherever the confirmed state is critical, which is what the dashboard's
        // incident card and the count above it both read.
        """
        INSERT INTO web_health.incident
            (id, endpoint_monitor_id, owner_subject_id, issue_key, severity, status,
             recurrence_count, opened_at, version)
        SELECT
            gen_random_uuid(),
            health.endpoint_monitor_id,
            coalesce(endpoint.owner_subject_id, website.owner_subject_id),
            'v1|' || monitor.monitor_type || '|Availability|baseline',
            'Critical',
            'Open',
            1,
            health.confirmed_at,
            1
        FROM web_health.endpoint_health AS health
        JOIN web_health.endpoint_monitor AS monitor ON monitor.id = health.endpoint_monitor_id
        JOIN web_health.endpoint AS endpoint ON endpoint.id = monitor.endpoint_id
        JOIN web_health.environment AS environment ON environment.id = endpoint.environment_id
        JOIN web_health.website AS website ON website.id = environment.website_id
        WHERE health.confirmed_status = 'Critical';
        """
    ];

    // ---------------------------------------------------------------- scenarios

    /// <summary>
    /// A scenario is a whole screen's worth of reads rather than one query, because that is the
    /// unit NFR-02 is stated in: a dashboard issuing seven fast queries and one slow one is a slow
    /// dashboard.
    /// </summary>
    private sealed record Scenario(
        string Name,
        string Purpose,
        bool IsDashboard,
        Func<IServiceProvider, CancellationToken, Task> RunAsync);

    private static IReadOnlyList<Scenario> BuildScenarios(BaselineFixture fixture) =>
    [
        new("Dashboard - unfiltered, 30 days",
            "The default landing view: every monitor the reader can see, over the default window.",
            true,
            (provider, token) => RunDashboardAsync(provider, fixture.Administrator, Query(), token)),

        new("Dashboard - one client, 30 days",
            "The most common narrowing, and the case where the filter has to reach an index.",
            true,
            (provider, token) => RunDashboardAsync(
                provider, fixture.Administrator, Query(clientId: fixture.FirstClientId), token)),

        new("Dashboard - unfiltered, 90 days",
            "Three times the default window: how the aggregate cost scales with the period.",
            true,
            (provider, token) => RunDashboardAsync(provider, fixture.Administrator, Query(days: 90), token)),

        new("Dashboard - viewer scoped to one client, 30 days",
            "The visibility scope on the critical path, which global access never exercises.",
            true,
            (provider, token) => RunDashboardAsync(provider, fixture.Viewer, Query(), token)),

        new("CSV export - unfiltered, 30 days",
            "The whole filtered set in one response, with per-monitor aggregates for every row.",
            false,
            async (provider, token) =>
            {
                var reader = provider.GetRequiredService<IReportingReader>();
                var export = await reader.ExportAsync(Query(), fixture.Administrator, token);
                _ = ReportCsv.Write(export);
            }),

        new("Report dataset - unfiltered, 90 days",
            "The screen dataset alone - summary, rows and the daily trend - over the widest window.",
            false,
            async (provider, token) =>
            {
                var reader = provider.GetRequiredService<IReportingReader>();
                _ = await reader.QueryAsync(Query(days: 90), fixture.Administrator, token);
            })
    ];

    /// <summary>Exactly the reads <c>HomeController.Index</c> performs, in the same order.</summary>
    private static async Task RunDashboardAsync(
        IServiceProvider provider,
        RegistryAccessContext access,
        ReportQuery query,
        CancellationToken cancellationToken)
    {
        var reporting = provider.GetRequiredService<IReportingReader>();
        var registry = provider.GetRequiredService<IRegistryReader>();
        var targets = provider.GetRequiredService<ITargetRegistryReader>();

        _ = await registry.ListClientsAsync(access, cancellationToken);
        _ = await registry.ListWebsitesAsync(access, cancellationToken: cancellationToken);
        _ = await targets.ListAllEnvironmentsAsync(access, cancellationToken);
        _ = await registry.ListOwnersAsync(cancellationToken: cancellationToken);
        _ = await reporting.QueryAsync(query, access, cancellationToken);
        _ = await reporting.QueryCertificateExpiryAsync(query, access, cancellationToken);
        _ = await reporting.QueryDiagnosticsAsync(query, access, cancellationToken);
        _ = await reporting.QueryActiveIncidentsAsync(query, access, 8, cancellationToken);
    }

    private static ReportQuery Query(Guid? clientId = null, int days = 30)
    {
        var normalized = ReportQueryNormalizer.Normalize(
            new(ClientId: clientId, WindowStart: AsOf.AddDays(-days), WindowEnd: AsOf),
            ReportMonitorTypes.All,
            AsOf);
        normalized.Succeeded.Should().BeTrue();
        return normalized.Query!;
    }

    // ------------------------------------------------------------- plan capture

    /// <summary>
    /// Runs each scenario once with <c>auto_explain</c> active and slices the server log around
    /// it, so every plan is attributed to the scenario that produced it without having to match
    /// statement text back to a caller.
    /// </summary>
    private static async Task<Dictionary<string, string>> CapturePlansAsync(
        ServiceProvider services,
        string connectionString,
        string serverLogPath,
        IReadOnlyList<Scenario> scenarios)
    {
        await SetAutoExplainAsync(connectionString, enabled: true);
        var plans = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var scenario in scenarios)
            {
                // A warm-up outside the captured slice, so the recorded plan is not dominated by
                // first-touch reads of pages no steady-state request would fault in.
                await RunScopedAsync(services, scenario);

                var offset = new FileInfo(serverLogPath).Length;
                await RunScopedAsync(services, scenario);
                plans[scenario.Name] = await ReadPlansAsync(serverLogPath, offset);
            }
        }
        finally
        {
            await SetAutoExplainAsync(connectionString, enabled: false);
        }

        return plans;
    }

    private static async Task SetAutoExplainAsync(string connectionString, bool enabled)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var settings = enabled
            ? """
              ALTER SYSTEM SET session_preload_libraries = 'auto_explain';
              ALTER SYSTEM SET auto_explain.log_min_duration = 0;
              ALTER SYSTEM SET auto_explain.log_analyze = on;
              ALTER SYSTEM SET auto_explain.log_buffers = on;
              ALTER SYSTEM SET auto_explain.log_nested_statements = on;
              ALTER SYSTEM SET auto_explain.log_format = 'text';
              """
            : "ALTER SYSTEM RESET session_preload_libraries;";
        await using var command = new NpgsqlCommand($"{settings}\nSELECT pg_reload_conf();", connection);
        await command.ExecuteNonQueryAsync();

        // session_preload_libraries is read when a session starts, and a reload does not reach
        // sessions that already exist. The measured services pool their connections, so without
        // this the pool would hand back connections opened during seeding - before auto_explain
        // was switched on - and the capture pass would log nothing at all.
        NpgsqlConnection.ClearAllPools();
    }

    private static async Task RunScopedAsync(ServiceProvider services, Scenario scenario)
    {
        await using var scope = services.CreateAsyncScope();
        await scenario.RunAsync(scope.ServiceProvider, CancellationToken.None);
    }

    private static async Task<string> ReadPlansAsync(string serverLogPath, long offset)
    {
        // The server writes plans to its own stderr; give it a moment to reach the file.
        await Task.Delay(TimeSpan.FromMilliseconds(750));

        await using var stream = new FileStream(
            serverLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync();

        var captured = new StringBuilder();
        var keeping = false;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var marker = trimmed.IndexOf("duration:", StringComparison.Ordinal);
            if (marker >= 0 && trimmed.Contains("plan:", StringComparison.Ordinal))
            {
                keeping = true;
                captured.AppendLine();
                captured.AppendLine(trimmed[marker..]);
                continue;
            }

            if (!keeping)
            {
                continue;
            }

            // A plan block is the indented continuation; the first unindented line ends it.
            if (trimmed.Length > 0 && !char.IsWhiteSpace(trimmed[0]))
            {
                keeping = false;
                continue;
            }

            captured.AppendLine(trimmed);
        }

        return captured.Length == 0 ? "(no plan captured)" : captured.ToString().Trim();
    }

    // ------------------------------------------------------------------ timing

    private static async Task<Dictionary<string, Timing>> MeasureAsync(
        ServiceProvider services,
        IReadOnlyList<Scenario> scenarios)
    {
        var timings = new Dictionary<string, Timing>(StringComparer.Ordinal);
        foreach (var scenario in scenarios)
        {
            await RunScopedAsync(services, scenario);

            var samples = new List<TimeSpan>(TimedIterations);
            for (var iteration = 0; iteration < TimedIterations; iteration++)
            {
                var stopwatch = Stopwatch.StartNew();
                await RunScopedAsync(services, scenario);
                stopwatch.Stop();
                samples.Add(stopwatch.Elapsed);
            }

            samples.Sort();
            timings[scenario.Name] = new(
                samples[0],
                samples[samples.Count / 2],
                samples[(int)Math.Ceiling(0.95 * samples.Count) - 1],
                samples[^1]);
        }

        return timings;
    }

    private sealed record Timing(
        TimeSpan Fastest,
        TimeSpan Median,
        TimeSpan Percentile95,
        TimeSpan Slowest);

    private sealed record BaselineFixture(
        RegistryAccessContext Administrator,
        RegistryAccessContext Viewer,
        Guid FirstClientId,
        int MonitorCount,
        int SampleCount);

    // ---------------------------------------------------------------- evidence

    private static string RenderEvidence(
        BaselineFixture fixture,
        IReadOnlyList<Scenario> scenarios,
        IReadOnlyDictionary<string, string> plans,
        IReadOnlyDictionary<string, Timing> timings)
    {
        var evidence = new StringBuilder();
        evidence.AppendLine("# Reporting query plans and performance baseline");
        evidence.AppendLine();
        evidence.AppendLine(
            "Generated by `scripts/run-reporting-performance-baseline.ps1`. Every figure below is "
            + "measured against a real PostgreSQL 18 cluster, not estimated.");
        evidence.AppendLine();
        evidence.AppendLine("## Fixture");
        evidence.AppendLine();
        evidence.AppendLine("| Property | Value |");
        evidence.AppendLine("|---|---|");
        evidence.AppendLine(Row(
            "Captured at (UTC)",
            DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture)));
        evidence.AppendLine(Row(
            "Clients / websites / environments",
            $"{ClientCount} / {ClientCount * WebsitesPerClient} / {ClientCount * WebsitesPerClient * 2}"));
        evidence.AppendLine(Row(
            "Endpoints",
            Count(ClientCount * WebsitesPerClient * 2 * EndpointsPerEnvironment)));
        evidence.AppendLine(Row("Monitors", Count(fixture.MonitorCount)));
        evidence.AppendLine(Row("Check results", Count(fixture.SampleCount)));
        evidence.AppendLine(Row("History retained", $"{HistoryDays} days"));
        evidence.AppendLine(Row("Iterations per scenario", Count(TimedIterations)));
        evidence.AppendLine();

        evidence.AppendLine("## Measured latency");
        evidence.AppendLine();
        evidence.AppendLine("| Scenario | Fastest | Median | P95 | Slowest | NFR-02 |");
        evidence.AppendLine("|---|---:|---:|---:|---:|---|");
        foreach (var scenario in scenarios)
        {
            var timing = timings[scenario.Name];
            var verdict = scenario.IsDashboard
                ? timing.Percentile95 < DashboardBudget ? "within budget" : "**over budget**"
                : "not a dashboard";
            evidence.AppendLine(
                $"| {scenario.Name} | {Ms(timing.Fastest)} | {Ms(timing.Median)} | "
                + $"{Ms(timing.Percentile95)} | {Ms(timing.Slowest)} | {verdict} |");
        }

        evidence.AppendLine();
        evidence.AppendLine("## Plans");
        evidence.AppendLine();
        evidence.AppendLine(
            "Captured with `auto_explain` (`log_analyze`, `log_buffers`, `log_nested_statements`), "
            + "so each plan is the one PostgreSQL chose for the statement the application issued. "
            + "The durations inside a plan include per-node instrumentation and are therefore "
            + "higher than the measured latency above.");

        foreach (var scenario in scenarios)
        {
            evidence.AppendLine();
            evidence.AppendLine($"### {scenario.Name}");
            evidence.AppendLine();
            evidence.AppendLine(scenario.Purpose);
            evidence.AppendLine();
            evidence.AppendLine("```");
            evidence.AppendLine(plans[scenario.Name]);
            evidence.AppendLine("```");
        }

        return evidence.ToString();
    }

    private static string Row(string name, string value) => $"| {name} | {value} |";

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Ms(TimeSpan value) =>
        $"{value.TotalMilliseconds.ToString("N0", CultureInfo.InvariantCulture)} ms";
}
