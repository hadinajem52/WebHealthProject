using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Identity;
using Npgsql;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Application.Administration;
using WebHealth.Application.Auditing;
using WebHealth.Application.Assignments;
using WebHealth.Infrastructure.Assignments;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Registry;
using Xunit;
using System.Text.Json;

namespace WebHealth.IntegrationTests.Support;

internal static class DatabaseFoundationAssertions
{
    private static readonly string[] ExpectedTables =
    [
        "audit_event",
        "access_grant",
        "client",
        "environment",
        "endpoint",
        "endpoint_monitor",
        "policy_profile",
        "tag",
        "website",
        "website_tag",
        "app_role",
        "app_role_claim",
        "app_user",
        "app_user_claim",
        "app_user_login",
        "app_user_role",
        "app_user_token"
        ,"owner_subject"
        ,"team"
        ,"team_member"
        ,"target_authorization"
    ];

    public static async Task VerifyAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString);
        await using var context = new ApplicationDbContext(options.Options);

        await context.Database.MigrateAsync();

        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await context.Database.GetAppliedMigrationsAsync()).Should().HaveCount(3);
        context.Model.GetEntityTypes().Should().HaveCount(21);

        var state = await ReadFoundationState(connectionString);
        state.SchemaExists.Should().BeTrue();
        state.Tables.Should().BeEquivalentTo(
            ExpectedTables.Append(DatabaseConventions.MigrationsHistoryTable));

        await VerifyIdentityBootstrapAsync(connectionString);
        await VerifyClientWebsiteRegistryAsync(connectionString);
        await VerifyEnvironmentEndpointRegistryAsync(connectionString);
        await VerifyPhaseOneUpgradeAndRepeatabilityAsync(context, connectionString);
    }

    private static async Task VerifyEnvironmentEndpointRegistryAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var environmentService = scope.ServiceProvider.GetRequiredService<IEnvironmentRegistryService>();
        var endpointService = scope.ServiceProvider.GetRequiredService<IEndpointRegistryService>();
        var targetReader = scope.ServiceProvider.GetRequiredService<ITargetRegistryReader>();
        var targetAuthorization = scope.ServiceProvider.GetRequiredService<ITargetAuthorizationService>();
        var monitoringEligibility = scope.ServiceProvider.GetRequiredService<IMonitoringEligibilityService>();

        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var developer = await database.Users.SingleAsync(user => user.Email == "registry-developer@example.test");
        var viewer = await database.Users.SingleAsync(user => user.Email == "registry-viewer@example.test");
        var developerOwnerId = await database.OwnerSubjects.Where(owner => owner.UserId == developer.Id)
            .Select(owner => owner.Id).SingleAsync();
        var administratorOwnerId = await database.OwnerSubjects.Where(owner => owner.UserId == administrator.Id)
            .Select(owner => owner.Id).SingleAsync();
        var website = await database.Websites.SingleAsync(candidate =>
            candidate.Client.Name == "Second Client" && candidate.Name == "Portal");
        var administratorAccess = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        var operationsAccess = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Operations]);

        var stagingResult = await environmentService.CreateAsync(
            new(website.Id, "  Staging  ", EnvironmentTypes.Staging, "HTTPS://Example.test:443/base", true),
            administratorAccess);
        stagingResult.Succeeded.Should().BeTrue();
        var stagingId = stagingResult.EntityId ?? throw new InvalidOperationException("Environment id was not returned.");
        (await environmentService.CreateAsync(
            new(website.Id, "staging", EnvironmentTypes.Test, null, true), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var staging = await database.Environments.SingleAsync(environment => environment.Id == stagingId);
        staging.Name.Should().Be("Staging");
        staging.BaseUrl.Should().Be("https://example.test/base");
        staging.IsProduction.Should().BeFalse();
        website = await database.Websites.SingleAsync(candidate => candidate.Id == website.Id);
        website.IsEnabled = true;
        await database.SaveChangesAsync();

        var endpointResult = await endpointService.CreateAsync(
            new(stagingId, " HTTPS://EXAMPLE.test:443/a/../Health?q=%41 ", developerOwnerId, true, null,
                TargetAuthorizationKinds.Owned, "Integration fixture owned by the project.", null),
            administratorAccess);
        endpointResult.Succeeded.Should().BeTrue();
        var endpointId = endpointResult.EntityId ?? throw new InvalidOperationException("Endpoint id was not returned.");
        (await endpointService.CreateAsync(
            new(stagingId, "https://example.test/Health?q=A", null, true, null,
                TargetAuthorizationKinds.Owned, "Integration fixture owned by the project.", null), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var endpoint = await database.Endpoints.Include(candidate => candidate.Monitors)
            .SingleAsync(candidate => candidate.Id == endpointId);
        endpoint.NormalizedUrl.Should().Be("https://example.test/Health?q=A");
        endpoint.NormalizedUrlHash.Should().HaveCount(32);
        endpoint.Monitors.Should().ContainSingle();
        var monitor = endpoint.Monitors.Single();
        monitor.MonitorType.Should().Be("HttpAvailability");
        monitor.ScheduleAnchor.Should().BeNull();
        monitor.NextDueAt.Should().BeNull();
        var authorizationEvidence = await database.TargetAuthorizations.SingleAsync(
            evidence => evidence.EndpointId == endpointId && evidence.RevokedAt == null);
        authorizationEvidence.AuthorizationKind.Should().Be(TargetAuthorizationKinds.Owned);
        authorizationEvidence.NormalizedHost.Should().Be("example.test");
        authorizationEvidence.Port.Should().Be(443);

        (await endpointService.CreateAsync(
            new(stagingId, "/relative", null, true, null, null, null, null), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        (await endpointService.CreateAsync(
            new(stagingId, "https://no-evidence.example.test/", null, true, null, null, null, null),
            administratorAccess)).Status.Should().Be(RegistryMutationStatus.ValidationFailed);

        var productionResult = await environmentService.CreateAsync(
            new(website.Id, "Production", EnvironmentTypes.Production, "https://example.test", true),
            administratorAccess);
        productionResult.Succeeded.Should().BeTrue();
        var productionId = productionResult.EntityId ?? throw new InvalidOperationException("Production environment id was not returned.");
        (await endpointService.CreateAsync(
            new(productionId, "http://legacy.example.test/", null, true, "Legacy appliance",
                TargetAuthorizationKinds.ExplicitPermission, "Permission ticket TEST-1", null), operationsAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var productionHttp = await endpointService.CreateAsync(
            new(productionId, "http://legacy.example.test/", null, true,
                "Legacy appliance requires HTTP during migration.", TargetAuthorizationKinds.ExplicitPermission,
                "Permission ticket TEST-2", null),
            administratorAccess);
        productionHttp.Succeeded.Should().BeTrue();

        var stagingHttp = await endpointService.CreateAsync(
            new(stagingId, "http://staging.example.test/", null, false, null, null, null, null),
            administratorAccess);
        stagingHttp.Succeeded.Should().BeTrue();
        database.ChangeTracker.Clear();
        staging = await database.Environments.SingleAsync(environment => environment.Id == stagingId);
        (await environmentService.UpdateAsync(
            new(staging.Id, staging.Name, EnvironmentTypes.Production, staging.BaseUrl, true, staging.Version), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);

        database.AccessGrants.Add(new AccessGrant
        {
            Id = Guid.NewGuid(),
            UserId = viewer.Id,
            AccessLevel = "Read",
            EndpointId = endpointId,
            EffectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-1),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = administrator.Id
        });
        await database.SaveChangesAsync();
        (await targetReader.ListEndpointsAsync(stagingId, new(viewer.Id, [ApplicationRoles.Viewer])))
            .Should().ContainSingle(item => item.Id == endpointId);
        var eligibilityState = await database.Endpoints.AsNoTracking()
            .Where(candidate => candidate.Id == endpointId)
            .Select(candidate => new
            {
                EndpointActive = candidate.DeletedAt == null && candidate.IsEnabled,
                EnvironmentActive = candidate.Environment.DeletedAt == null && candidate.Environment.IsActive,
                WebsiteActive = candidate.Environment.Website.DeletedAt == null && candidate.Environment.Website.IsEnabled,
                MonitorActive = candidate.Monitors.Any(monitor => monitor.DeletedAt == null && monitor.IsEnabled),
                EvidenceActive = candidate.TargetAuthorizations.Any(evidence =>
                    evidence.RevokedAt == null
                    && evidence.EffectiveFrom <= DateTimeOffset.UtcNow
                    && (evidence.ExpiresAt == null || evidence.ExpiresAt > DateTimeOffset.UtcNow)
                    && evidence.NormalizedHost == candidate.NormalizedHost
                    && evidence.Port == candidate.EffectivePort)
            }).SingleAsync();
        eligibilityState.EndpointActive.Should().BeTrue();
        eligibilityState.EnvironmentActive.Should().BeTrue();
        eligibilityState.WebsiteActive.Should().BeTrue();
        eligibilityState.MonitorActive.Should().BeTrue();
        eligibilityState.EvidenceActive.Should().BeTrue();
        (await monitoringEligibility.IsEndpointEligibleAsync(endpointId)).Should().BeTrue();
        (await targetReader.ListEndpointsAsync(
            stagingId, new(developer.Id, [ApplicationRoles.DeveloperSupport])))
            .Should().Contain(item => item.Id == endpointId);
        (await targetAuthorization.CanTestEndpointAsync(endpointId, new(developer.Id, [ApplicationRoles.DeveloperSupport])))
            .Should().BeTrue();
        (await targetAuthorization.CanTestEndpointAsync(endpointId, new(viewer.Id, [ApplicationRoles.Viewer])))
            .Should().BeFalse();

        website = await database.Websites.SingleAsync(candidate => candidate.Id == website.Id);
        website.IsEnabled = false;
        website.Version++;
        await database.SaveChangesAsync();
        (await monitoringEligibility.IsEndpointEligibleAsync(endpointId)).Should().BeFalse();
        (await database.EndpointMonitors.Where(candidate => candidate.EndpointId == endpointId)
            .Select(candidate => candidate.IsEnabled).SingleAsync()).Should().BeTrue();
        website.IsEnabled = true;
        website.Version++;
        await database.SaveChangesAsync();
        (await monitoringEligibility.IsEndpointEligibleAsync(endpointId)).Should().BeTrue();

        website = await database.Websites.SingleAsync(candidate => candidate.Id == website.Id);
        website.OwnerSubjectId = developerOwnerId;
        website.Version++;
        await database.SaveChangesAsync();
        var overriddenEndpoint = await endpointService.CreateAsync(
            new(stagingId, "https://override.example.test/", administratorOwnerId, true, null,
                TargetAuthorizationKinds.Owned, "Administrator-owned integration fixture.", null),
            administratorAccess);
        overriddenEndpoint.Succeeded.Should().BeTrue();
        var overriddenEndpointId = overriddenEndpoint.EntityId!.Value;
        (await targetReader.ListEndpointsAsync(stagingId,
            new(developer.Id, [ApplicationRoles.DeveloperSupport])))
            .Should().NotContain(item => item.Id == overriddenEndpointId);
        (await targetAuthorization.CanTestEndpointAsync(
            overriddenEndpointId, new(developer.Id, [ApplicationRoles.DeveloperSupport])))
            .Should().BeFalse();

        var staleEndpoint = await endpointService.UpdateAsync(
            new(endpointId, endpoint.DisplayUrl, endpoint.OwnerSubjectId, endpoint.IsEnabled, null,
                TargetAuthorizationKinds.Owned, "Integration fixture owned by the project.", null, 0),
            administratorAccess);
        staleEndpoint.Status.Should().Be(RegistryMutationStatus.ConcurrencyConflict);

        var staleEnvironment = await environmentService.UpdateAsync(
            new(staging.Id, staging.Name, staging.EnvironmentType, staging.BaseUrl, staging.IsActive, 0),
            administratorAccess);
        staleEnvironment.Status.Should().Be(RegistryMutationStatus.ConcurrencyConflict);

        await VerifyEnvironmentTypeConstraintAsync(connectionString, stagingId);
        await VerifyProductionHttpConstraintAsync(connectionString, productionId, developer.Id, administrator.Id);
        await VerifyProductionHttpTransitionsAsync(
            connectionString, website.Id, productionId, developer.Id, administrator.Id);
        await VerifyMonitorPolicyConstraintAsync(connectionString, endpointId, administrator.Id);

        database.ChangeTracker.Clear();
        endpoint = await database.Endpoints.SingleAsync(candidate => candidate.Id == endpointId);
        var updateEndpoint = await endpointService.UpdateAsync(
            new(endpoint.Id, "https://example.test/health-v2", endpoint.OwnerSubjectId, true, null,
                TargetAuthorizationKinds.Owned, "Updated integration fixture ownership evidence.", null, endpoint.Version),
            administratorAccess);
        updateEndpoint.Succeeded.Should().BeTrue(string.Join(" ", updateEndpoint.Errors));
        endpoint = await database.Endpoints.SingleAsync(candidate => candidate.Id == endpointId);
        (await endpointService.DisableAsync(new(endpoint.Id, endpoint.Version), administratorAccess)).Succeeded.Should().BeTrue();
        endpoint = await database.Endpoints.SingleAsync(candidate => candidate.Id == endpointId);
        (await endpointService.DeleteAsync(new(endpoint.Id, endpoint.Version), administratorAccess)).Succeeded.Should().BeTrue();
        (await targetReader.ListEndpointsAsync(stagingId, administratorAccess)).Should().NotContain(item => item.Id == endpointId);
        (await targetReader.ListDeletedEndpointsAsync(administratorAccess)).Should().Contain(item => item.Id == endpointId);
        endpoint = await database.Endpoints.SingleAsync(candidate => candidate.Id == endpointId);
        (await endpointService.RestoreAsync(new(endpoint.Id, endpoint.Version), administratorAccess)).Succeeded.Should().BeTrue();

        database.ChangeTracker.Clear();
        staging = await database.Environments.SingleAsync(environment => environment.Id == stagingId);
        (await environmentService.UpdateAsync(
            new(staging.Id, "Staging Updated", EnvironmentTypes.Staging, staging.BaseUrl, true, staging.Version),
            administratorAccess)).Succeeded.Should().BeTrue();
        staging = await database.Environments.SingleAsync(environment => environment.Id == stagingId);
        (await environmentService.DisableAsync(new(staging.Id, staging.Version), administratorAccess)).Succeeded.Should().BeTrue();
        (await targetAuthorization.CanTestEndpointAsync(endpointId,
            new(developer.Id, [ApplicationRoles.DeveloperSupport]))).Should().BeFalse();
        (await database.EndpointMonitors.Where(candidate => candidate.EndpointId == endpointId)
            .Select(candidate => candidate.IsEnabled).SingleAsync()).Should().BeTrue();
        staging = await database.Environments.SingleAsync(environment => environment.Id == stagingId);
        (await environmentService.DeleteAsync(new(staging.Id, staging.Version), administratorAccess)).Succeeded.Should().BeTrue();
        (await targetReader.ListDeletedEnvironmentsAsync(administratorAccess)).Should().Contain(item => item.Id == stagingId);
        staging = await database.Environments.SingleAsync(environment => environment.Id == stagingId);
        (await environmentService.RestoreAsync(new(staging.Id, staging.Version), administratorAccess)).Succeeded.Should().BeTrue();

        var actions = await database.AuditEvents.Where(audit =>
                audit.EntityIdentifier == endpointId.ToString() || audit.EntityIdentifier == stagingId.ToString())
            .Select(audit => audit.Action).ToListAsync();
        actions.Should().Contain(["environment.created", "environment.updated", "environment.disabled", "environment.deleted", "environment.restored"]);
        actions.Should().Contain(["endpoint.created", "endpoint.updated", "endpoint.disabled", "endpoint.deleted", "endpoint.restored"]);

        var endpointAuditPayloads = await database.AuditEvents
            .Where(audit => audit.EntityIdentifier == endpointId.ToString())
            .Select(audit => new { audit.BeforeValues, audit.AfterValues })
            .ToListAsync();
        endpointAuditPayloads.Should().OnlyContain(payload =>
            !(payload.BeforeValues ?? string.Empty).Contains("?q=", StringComparison.Ordinal)
            && !(payload.AfterValues ?? string.Empty).Contains("?q=", StringComparison.Ordinal)
            && !(payload.BeforeValues ?? string.Empty).Contains("Legacy appliance", StringComparison.Ordinal)
            && !(payload.AfterValues ?? string.Empty).Contains("Legacy appliance", StringComparison.Ordinal)
            && !(payload.BeforeValues ?? string.Empty).Contains("Integration fixture owned", StringComparison.Ordinal)
            && !(payload.AfterValues ?? string.Empty).Contains("Integration fixture owned", StringComparison.Ordinal)
            && !(payload.BeforeValues ?? string.Empty).Contains("Updated integration fixture", StringComparison.Ordinal)
            && !(payload.AfterValues ?? string.Empty).Contains("Updated integration fixture", StringComparison.Ordinal));

    }

    private static async Task VerifyPhaseOneUpgradeAndRepeatabilityAsync(
        ApplicationDbContext database,
        string connectionString)
    {
        database.ChangeTracker.Clear();
        await database.Database.MigrateAsync("InitialFoundation");
        (await database.Database.GetAppliedMigrationsAsync()).Should().ContainSingle();
        var baseline = await ReadFoundationState(connectionString);
        baseline.Tables.Should().Equal(DatabaseConventions.MigrationsHistoryTable);

        await database.Database.MigrateAsync();
        var applied = (await database.Database.GetAppliedMigrationsAsync()).ToArray();
        applied.Should().HaveCount(3);
        (await database.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        var upgraded = await ReadFoundationState(connectionString);
        upgraded.Tables.Should().BeEquivalentTo(
            ExpectedTables.Append(DatabaseConventions.MigrationsHistoryTable));

        await database.Database.MigrateAsync();
        (await database.Database.GetAppliedMigrationsAsync()).Should().Equal(applied);
    }

    private static async Task VerifyEnvironmentTypeConstraintAsync(string connectionString, Guid environmentId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE web_health.environment SET environment_type = 'Production', is_production = FALSE WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", environmentId);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.Should().Be("ck_environment_type_matches_production");
    }

    private static async Task VerifyProductionHttpConstraintAsync(
        string connectionString, Guid environmentId, Guid nonAdministratorId, Guid actorId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        const string sql = """
            INSERT INTO web_health.endpoint
                (id, environment_id, display_url, normalized_url, normalized_url_hash, normalized_host,
                 effective_port, normalization_version,
                 is_enabled, http_exception_reason, http_exception_approved_by_user_id, http_exception_approved_at,
                 created_at, created_by_user_id, updated_at, updated_by_user_id, version)
            VALUES (@id, @environment_id, 'http://unsafe.example.test/', 'http://unsafe.example.test/', @hash,
                    'unsafe.example.test', 80, 1,
                    TRUE, 'Not approved by an administrator', @approver, now(), now(), @actor, now(), @actor, 1);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("environment_id", environmentId);
        command.Parameters.AddWithValue("hash", new byte[32]);
        command.Parameters.AddWithValue("approver", nonAdministratorId);
        command.Parameters.AddWithValue("actor", actorId);
        await command.ExecuteNonQueryAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        exception.ConstraintName.Should().Be("ck_production_http_endpoint_admin_exception");
    }

    private static async Task VerifyProductionHttpTransitionsAsync(
        string connectionString,
        Guid websiteId,
        Guid productionEnvironmentId,
        Guid nonAdministratorId,
        Guid actorId)
    {
        var nonProductionEnvironmentId = Guid.NewGuid();
        var httpEndpointId = Guid.NewGuid();
        var httpsEndpointId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            const string setupSql = """
                INSERT INTO web_health.environment
                    (id, website_id, name, normalized_name, normalization_version, environment_type,
                     is_production, is_active, created_at, created_by_user_id, updated_at, updated_by_user_id, version)
                VALUES (@environment_id, @website_id, 'Transition test', 'transition test', 1, 'Test',
                        FALSE, TRUE, now(), @actor, now(), @actor, 1);

                INSERT INTO web_health.endpoint
                    (id, environment_id, display_url, normalized_url, normalized_url_hash, normalized_host,
                     effective_port, normalization_version, is_enabled, http_exception_reason,
                     http_exception_approved_by_user_id, http_exception_approved_at,
                     created_at, created_by_user_id, updated_at, updated_by_user_id, version)
                VALUES (@http_endpoint_id, @environment_id, 'http://transition.example.test/',
                        'http://transition.example.test/', @http_hash, 'transition.example.test', 80, 1, TRUE,
                        'Evidence recorded before Production', @approver, now(), now(), @actor, now(), @actor, 1);

                INSERT INTO web_health.endpoint
                    (id, environment_id, display_url, normalized_url, normalized_url_hash, normalized_host,
                     effective_port, normalization_version, is_enabled, http_exception_reason,
                     http_exception_approved_by_user_id, http_exception_approved_at,
                     created_at, created_by_user_id, updated_at, updated_by_user_id, version)
                VALUES (@https_endpoint_id, @production_environment_id, 'https://scheme-change.example.test/',
                        'https://scheme-change.example.test/', @https_hash, 'scheme-change.example.test', 443, 1, TRUE,
                        'Irrelevant evidence while HTTPS', @approver, now(), now(), @actor, now(), @actor, 1);
                """;
            await using var setup = new NpgsqlCommand(setupSql, connection);
            setup.Parameters.AddWithValue("environment_id", nonProductionEnvironmentId);
            setup.Parameters.AddWithValue("website_id", websiteId);
            setup.Parameters.AddWithValue("http_endpoint_id", httpEndpointId);
            setup.Parameters.AddWithValue("https_endpoint_id", httpsEndpointId);
            setup.Parameters.AddWithValue("production_environment_id", productionEnvironmentId);
            setup.Parameters.AddWithValue("http_hash", Enumerable.Repeat((byte)1, 32).ToArray());
            setup.Parameters.AddWithValue("https_hash", Enumerable.Repeat((byte)2, 32).ToArray());
            setup.Parameters.AddWithValue("approver", nonAdministratorId);
            setup.Parameters.AddWithValue("actor", actorId);
            await setup.ExecuteNonQueryAsync();
        }

        await AssertProductionTransitionRejectedAsync(
            connectionString,
            "UPDATE web_health.environment SET environment_type = 'Production', is_production = TRUE WHERE id = @id",
            nonProductionEnvironmentId);
        await AssertProductionTransitionRejectedAsync(
            connectionString,
            "UPDATE web_health.endpoint SET environment_id = @production_id WHERE id = @id",
            httpEndpointId,
            productionEnvironmentId);
        await AssertProductionTransitionRejectedAsync(
            connectionString,
            """
                UPDATE web_health.endpoint
                SET display_url = 'http://scheme-change.example.test/',
                    normalized_url = 'http://scheme-change.example.test/',
                    normalized_url_hash = @hash,
                    effective_port = 80
                WHERE id = @id
                """,
            httpsEndpointId,
            hash: Enumerable.Repeat((byte)3, 32).ToArray());
    }

    private static async Task AssertProductionTransitionRejectedAsync(
        string connectionString,
        string sql,
        Guid endpointOrEnvironmentId,
        Guid? productionEnvironmentId = null,
        byte[]? hash = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", endpointOrEnvironmentId);
        if (productionEnvironmentId is not null)
        {
            command.Parameters.AddWithValue("production_id", productionEnvironmentId.Value);
        }

        if (hash is not null)
        {
            command.Parameters.AddWithValue("hash", hash);
        }

        await command.ExecuteNonQueryAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        exception.ConstraintName.Should().Be("ck_production_http_endpoint_admin_exception");
    }

    private static async Task VerifyMonitorPolicyConstraintAsync(string connectionString, Guid endpointId, Guid actorId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        const string sql = """
            INSERT INTO web_health.endpoint_monitor
                (id, endpoint_id, policy_profile_id, monitor_type, bounded_overrides, configuration_fingerprint,
                 interval_seconds, timeout_seconds, failure_confirmation_count, recovery_confirmation_count,
                 is_enabled, created_at, created_by_user_id, updated_at, updated_by_user_id, version)
            VALUES (@id, @endpoint_id, 'fd3c8021-ff54-4f31-a3ad-2010b7b193dd', 'SslCertificate', '{}', repeat('0', 64),
                    900, 30, 2, 2, FALSE, now(), @actor, now(), @actor, 1);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("endpoint_id", endpointId);
        command.Parameters.AddWithValue("actor", actorId);
        await command.ExecuteNonQueryAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        exception.ConstraintName.Should().Be("ck_endpoint_monitor_policy_type");
    }

    private static async Task VerifyClientWebsiteRegistryAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WebHealth"] = connectionString
            })
            .Build();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
        var clients = scope.ServiceProvider.GetRequiredService<IClientRegistryService>();
        var websites = scope.ServiceProvider.GetRequiredService<IWebsiteRegistryService>();
        var reader = scope.ServiceProvider.GetRequiredService<IRegistryReader>();

        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var administratorOwnerId = await database.OwnerSubjects
            .Where(owner => owner.UserId == administrator.Id)
            .Select(owner => owner.Id)
            .SingleAsync();
        var administratorAccess = new RegistryAccessContext(
            administrator.Id,
            [ApplicationRoles.Administrator]);

        var developerResult = await users.CreateUserAsync(
            new CreateManagedUser(
                "Registry Developer",
                "registry-developer@example.test",
                $"Registry-9!{Guid.NewGuid():N}",
                [ApplicationRoles.DeveloperSupport]),
            administrator.Id);
        developerResult.Succeeded.Should().BeTrue();
        var viewerResult = await users.CreateUserAsync(
            new CreateManagedUser(
                "Registry Viewer",
                "registry-viewer@example.test",
                $"Registry-9!{Guid.NewGuid():N}",
                [ApplicationRoles.Viewer]),
            administrator.Id);
        viewerResult.Succeeded.Should().BeTrue();

        var developer = await database.Users.SingleAsync(user => user.Email == "registry-developer@example.test");
        var viewer = await database.Users.SingleAsync(user => user.Email == "registry-viewer@example.test");
        var developerOwnerId = await database.OwnerSubjects
            .Where(owner => owner.UserId == developer.Id)
            .Select(owner => owner.Id)
            .SingleAsync();

        var firstClient = await clients.CreateAsync(
            new CreateClient("  Alpha Client  ", developerOwnerId, "  scoped notes  "),
            administratorAccess);
        firstClient.Succeeded.Should().BeTrue();
        var firstClientId = firstClient.EntityId ?? throw new InvalidOperationException("Client id was not returned.");
        var duplicateClient = await clients.CreateAsync(
            new CreateClient("alpha client", administratorOwnerId, null),
            administratorAccess);
        duplicateClient.Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var secondClient = await clients.CreateAsync(
            new CreateClient("Second Client", administratorOwnerId, null),
            administratorAccess);
        secondClient.Succeeded.Should().BeTrue();
        var secondClientId = secondClient.EntityId ?? throw new InvalidOperationException("Client id was not returned.");

        var persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        persistedClient.Name.Should().Be("Alpha Client");
        persistedClient.Notes.Should().Be("scoped notes");
        var staleClientUpdate = await clients.UpdateAsync(
            new UpdateClient(
                persistedClient.Id,
                persistedClient.Name,
                persistedClient.OwnerSubjectId,
                persistedClient.Notes,
                true,
                0),
            administratorAccess);
        staleClientUpdate.Status.Should().Be(RegistryMutationStatus.ConcurrencyConflict);

        var firstWebsite = await websites.CreateAsync(
            new CreateWebsite(firstClientId, "  Portal  ", developerOwnerId, " ASP.NET ", false,
                ["  Europe ", "ASP.NET", "europe"]),
            administratorAccess);
        firstWebsite.Succeeded.Should().BeTrue();
        var firstWebsiteId = firstWebsite.EntityId ?? throw new InvalidOperationException("Website id was not returned.");
        (await websites.CreateAsync(
            new CreateWebsite(firstClientId, "portal", developerOwnerId, null, false, []),
            administratorAccess)).Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        (await websites.CreateAsync(
            new CreateWebsite(secondClientId, "Portal", administratorOwnerId, null, false, []),
            administratorAccess)).Succeeded.Should().BeTrue();

        var persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var enableWithoutEnvironment = await websites.UpdateAsync(
            new UpdateWebsite(
                persistedWebsite.Id,
                persistedWebsite.Name,
                persistedWebsite.OwnerSubjectId,
                persistedWebsite.TechnologyCms,
                true,
                persistedWebsite.Version,
                ["ASP.NET", "Europe"]),
            administratorAccess);
        enableWithoutEnvironment.Status.Should().Be(RegistryMutationStatus.ValidationFailed);

        await VerifyEnabledWebsiteConstraintAsync(connectionString, persistedWebsite.Id);
        database.ChangeTracker.Clear();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var now = DateTimeOffset.UtcNow;
        database.Environments.Add(new WebsiteEnvironment
        {
            Id = Guid.NewGuid(),
            WebsiteId = persistedWebsite.Id,
            Name = "Production",
            NormalizedName = "production",
            NormalizationVersion = 1,
            EnvironmentType = "Production",
            IsProduction = true,
            BaseUrl = "https://example.test",
            IsActive = true,
            CreatedAt = now,
            CreatedByUserId = administrator.Id,
            UpdatedAt = now,
            UpdatedByUserId = administrator.Id,
            Version = 1
        });
        await database.SaveChangesAsync();
        (await websites.UpdateAsync(
            new UpdateWebsite(
                persistedWebsite.Id,
                persistedWebsite.Name,
                persistedWebsite.OwnerSubjectId,
                persistedWebsite.TechnologyCms,
                true,
                persistedWebsite.Version,
                ["ASP.NET", "Europe"]),
            administratorAccess)).Succeeded.Should().BeTrue();

        database.AccessGrants.Add(new AccessGrant
        {
            Id = Guid.NewGuid(),
            UserId = viewer.Id,
            AccessLevel = "Read",
            ClientId = firstClientId,
            EffectiveFrom = now.AddMinutes(-1),
            CreatedAt = now,
            CreatedByUserId = administrator.Id
        });
        await database.SaveChangesAsync();

        var developerClients = await reader.ListClientsAsync(new(
            developer.Id,
            [ApplicationRoles.DeveloperSupport]));
        developerClients.Select(client => client.Id).Should().Equal(firstClientId);
        database.ChangeTracker.Clear();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        (await websites.UpdateAsync(
            new UpdateWebsite(
                persistedWebsite.Id,
                persistedWebsite.Name,
                persistedWebsite.OwnerSubjectId,
                persistedWebsite.TechnologyCms,
                persistedWebsite.IsEnabled,
                persistedWebsite.Version,
                ["ASP.NET", "Europe"]),
            new RegistryAccessContext(developer.Id, [ApplicationRoles.DeveloperSupport])))
            .Status.Should().Be(RegistryMutationStatus.Forbidden);
        var viewerWebsites = await reader.ListWebsitesAsync(new(viewer.Id, [ApplicationRoles.Viewer]));
        viewerWebsites.Should().Contain(website => website.Id == firstWebsiteId);
        viewerWebsites.Should().NotContain(website => website.ClientId == secondClientId);
        viewerWebsites.Single(website => website.Id == firstWebsiteId).Tags.Should().Equal("ASP.NET", "Europe");
        var europeTag = await database.Tags.SingleAsync(tag => tag.NormalizedName == "EUROPE");
        (await database.Tags.CountAsync()).Should().Be(2, "repeated tag input is normalized and stored once");
        (await reader.ListWebsitesAsync(
            new(viewer.Id, [ApplicationRoles.Viewer]), europeTag.Id))
            .Should().ContainSingle(website => website.Id == firstWebsiteId);
        (await reader.ListTagsAsync(new(viewer.Id, [ApplicationRoles.Viewer])))
            .Should().ContainSingle(tag => tag.Id == europeTag.Id && tag.WebsiteCount == 1);
        await VerifyTagUniquenessConstraintAsync(connectionString, europeTag.Id);

        var websiteAudit = await database.AuditEvents.AsNoTracking()
            .Where(auditEvent => auditEvent.EntityIdentifier == firstWebsiteId.ToString())
            .OrderByDescending(auditEvent => auditEvent.OccurredAt)
            .FirstAsync();
        websiteAudit.AfterValues.Should().Contain("ASP.NET").And.Contain("Europe");

        var startConcurrentTagWrites = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrentCreates = new[]
        {
            CreateWebsiteWithSharedTagAsync(
                connectionString, firstClientId, "Concurrent Alpha", developerOwnerId,
                administratorAccess, startConcurrentTagWrites.Task),
            CreateWebsiteWithSharedTagAsync(
                connectionString, secondClientId, "Concurrent Beta", administratorOwnerId,
                administratorAccess, startConcurrentTagWrites.Task)
        };
        startConcurrentTagWrites.SetResult();
        var concurrentResults = await Task.WhenAll(concurrentCreates);
        concurrentResults.Should().OnlyContain(result => result.Succeeded);
        var concurrentTag = await database.Tags.SingleAsync(tag => tag.NormalizedName == "CONCURRENT");
        (await database.WebsiteTags.CountAsync(websiteTag => websiteTag.TagId == concurrentTag.Id))
            .Should().Be(2);

        database.ChangeTracker.Clear();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var disabled = await websites.DisableAsync(new(persistedWebsite.Id, persistedWebsite.Version), administratorAccess);
        disabled.Succeeded.Should().BeTrue();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var deleted = await websites.DeleteAsync(new(persistedWebsite.Id, persistedWebsite.Version), administratorAccess);
        deleted.Succeeded.Should().BeTrue();
        (await reader.ListWebsitesAsync(administratorAccess))
            .Should().NotContain(website => website.Id == firstWebsiteId);
        (await reader.ListDeletedWebsitesAsync(administratorAccess))
            .Should().ContainSingle(website => website.Id == firstWebsiteId);
        (await reader.ListDeletedWebsitesAsync(new(developer.Id, [ApplicationRoles.DeveloperSupport])))
            .Should().BeEmpty();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var replacementWebsite = await websites.CreateAsync(
            new CreateWebsite(
                firstClientId,
                persistedWebsite.Name,
                persistedWebsite.OwnerSubjectId,
                null,
                false,
                []),
            administratorAccess);
        replacementWebsite.Succeeded.Should().BeTrue();
        (await websites.RestoreAsync(new(persistedWebsite.Id, persistedWebsite.Version), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var replacementWebsiteEntity = await database.Websites.SingleAsync(website =>
            website.Id == replacementWebsite.EntityId);
        (await websites.DeleteAsync(
            new(replacementWebsiteEntity.Id, replacementWebsiteEntity.Version), administratorAccess))
            .Succeeded.Should().BeTrue();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var restored = await websites.RestoreAsync(new(persistedWebsite.Id, persistedWebsite.Version), administratorAccess);
        restored.Succeeded.Should().BeTrue();

        var websiteAuditActions = await database.AuditEvents
            .Where(audit => audit.EntityIdentifier == persistedWebsite.Id.ToString())
            .Select(audit => audit.Action)
            .ToListAsync();
        websiteAuditActions.Should().Contain([
            "website.created",
            "website.updated",
            "website.disabled",
            "website.deleted",
            "website.restored"]);

        database.ChangeTracker.Clear();
        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        (await clients.UpdateAsync(
            new UpdateClient(
                persistedClient.Id,
                persistedClient.Name,
                persistedClient.OwnerSubjectId,
                "changed private notes",
                true,
                persistedClient.Version),
            administratorAccess)).Succeeded.Should().BeTrue();
        var notesAuditJson = await database.AuditEvents
            .Where(audit => audit.EntityIdentifier == firstClientId.ToString()
                && audit.Action == "client.updated")
            .OrderByDescending(audit => audit.OccurredAt)
            .Select(audit => audit.AfterValues)
            .FirstAsync();
        using (var notesAudit = JsonDocument.Parse(notesAuditJson!))
        {
            notesAudit.RootElement.GetProperty("notesChanged").GetBoolean().Should().BeTrue();
            notesAuditJson.Should().NotContain("changed private notes");
        }

        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        (await clients.DisableAsync(new(persistedClient.Id, persistedClient.Version), administratorAccess))
            .Succeeded.Should().BeTrue();
        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        (await clients.DeleteAsync(new(persistedClient.Id, persistedClient.Version), administratorAccess))
            .Succeeded.Should().BeTrue();
        (await reader.ListClientsAsync(administratorAccess))
            .Should().NotContain(client => client.Id == firstClientId);
        (await reader.ListDeletedClientsAsync(administratorAccess))
            .Should().ContainSingle(client => client.Id == firstClientId);
        (await reader.ListDeletedClientsAsync(new(viewer.Id, [ApplicationRoles.Viewer])))
            .Should().BeEmpty();
        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        var replacementClient = await clients.CreateAsync(
            new CreateClient(persistedClient.Name, persistedClient.OwnerSubjectId, null),
            administratorAccess);
        replacementClient.Succeeded.Should().BeTrue();
        (await clients.RestoreAsync(new(persistedClient.Id, persistedClient.Version), administratorAccess))
            .Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var replacementClientEntity = await database.Clients.SingleAsync(client =>
            client.Id == replacementClient.EntityId);
        (await clients.DeleteAsync(
            new(replacementClientEntity.Id, replacementClientEntity.Version), administratorAccess))
            .Succeeded.Should().BeTrue();
        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        (await clients.RestoreAsync(new(persistedClient.Id, persistedClient.Version), administratorAccess))
            .Succeeded.Should().BeTrue();

        var clientAuditActions = await database.AuditEvents
            .Where(audit => audit.EntityIdentifier == firstClientId.ToString())
            .Select(audit => audit.Action)
            .ToListAsync();
        clientAuditActions.Should().Contain([
            "client.created",
            "client.updated",
            "client.disabled",
            "client.deleted",
            "client.restored"]);
    }

    private static async Task VerifyEnabledWebsiteConstraintAsync(
        string connectionString,
        Guid websiteId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE web_health.website SET is_enabled = TRUE WHERE id = @id",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", websiteId);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.Should().Be("ck_website_enabled_requires_active_environment");
    }

    private static async Task<RegistryMutationResult> CreateWebsiteWithSharedTagAsync(
        string connectionString,
        Guid clientId,
        string websiteName,
        Guid ownerSubjectId,
        RegistryAccessContext access,
        Task start)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var websiteService = scope.ServiceProvider.GetRequiredService<IWebsiteRegistryService>();
        await start;
        return await websiteService.CreateAsync(
            new CreateWebsite(clientId, websiteName, ownerSubjectId, null, false, ["Concurrent"]),
            access);
    }

    private static async Task VerifyTagUniquenessConstraintAsync(string connectionString, Guid tagId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO web_health.tag
                (id, name, normalized_name, normalization_version, created_at, created_by_user_id, version)
            SELECT gen_random_uuid(), name, normalized_name, normalization_version, now(), created_by_user_id, 1
            FROM web_health.tag
            WHERE id = @tag_id;
            """;
        command.Parameters.AddWithValue("tag_id", tagId);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.ConstraintName.Should().Be("ix_tag_normalized_name_normalization_version");
    }

    private static async Task VerifyIdentityBootstrapAsync(string connectionString)
    {
        var password = $"Integration-9!{Guid.NewGuid():N}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WebHealth"] = connectionString,
                ["BootstrapAdmin:Email"] = "bootstrap@example.test",
                ["BootstrapAdmin:DisplayName"] = "Bootstrap Administrator",
                ["BootstrapAdmin:Password"] = password
            })
            .Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();
            await bootstrapper.BootstrapAsync();
            await bootstrapper.BootstrapAsync();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var roles = await roleManager.Roles.OrderBy(role => role.Name).ToListAsync();
            roles.Select(role => (role.Id, role.Name)).Should().BeEquivalentTo(
                ApplicationRoles.All.Select(role => (role.Id, (string?)role.Name)));

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync("bootstrap@example.test");
            user.Should().NotBeNull();
            user!.PasswordHash.Should().NotBeNullOrWhiteSpace().And.NotBe(password);
            (await userManager.CheckPasswordAsync(user, password)).Should().BeTrue();
            (await userManager.IsInRoleAsync(user, ApplicationRoles.Administrator)).Should().BeTrue();

            var signInManager = scope.ServiceProvider
                .GetRequiredService<SignInManager<ApplicationUser>>();
            user.IsDisabled = true;
            (await signInManager.CanSignInAsync(user)).Should().BeFalse();
            user.IsDisabled = false;

            var administration = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
            var managedPassword = $"Managed-8!{Guid.NewGuid():N}";
            var createResult = await administration.CreateUserAsync(
                new CreateManagedUser(
                    "Managed Viewer",
                    "managed-viewer@example.test",
                    managedPassword,
                    [ApplicationRoles.Viewer]),
                user.Id);
            createResult.Succeeded.Should().BeTrue();

            var managedUser = await userManager.FindByIdAsync(createResult.UserId!.Value.ToString());
            managedUser.Should().NotBeNull();
            var originalSecurityStamp = managedUser!.SecurityStamp;
            var existingPrincipal = await signInManager.CreateUserPrincipalAsync(managedUser);
            var replacementPassword = $"Replacement-7!{Guid.NewGuid():N}";

            var disableResult = await administration.UpdateUserAsync(
                new UpdateManagedUser(
                    managedUser.Id,
                    "Managed Viewer",
                    true,
                    [ApplicationRoles.Operations],
                    replacementPassword),
                user.Id);
            disableResult.Succeeded.Should().BeTrue();

            managedUser = await userManager.FindByIdAsync(managedUser.Id.ToString());
            managedUser!.IsDisabled.Should().BeTrue();
            managedUser.SecurityStamp.Should().NotBe(originalSecurityStamp);
            (await userManager.GetRolesAsync(managedUser)).Should().Equal(ApplicationRoles.Operations);
            (await userManager.CheckPasswordAsync(managedUser, replacementPassword)).Should().BeTrue();
            managedUser.PasswordHash.Should().NotBe(replacementPassword);
            (await signInManager.ValidateSecurityStampAsync(existingPrincipal)).Should().BeNull();

            var selfDisableResult = await administration.UpdateUserAsync(
                new UpdateManagedUser(
                    user.Id,
                    user.DisplayName,
                    true,
                    [ApplicationRoles.Administrator]),
                user.Id);
            selfDisableResult.Succeeded.Should().BeFalse();

            var roleOnlyPassword = $"Role-only-6!{Guid.NewGuid():N}";
            var roleOnlyCreateResult = await administration.CreateUserAsync(
                new CreateManagedUser(
                    "Role-only Administrator",
                    "role-only@example.test",
                    roleOnlyPassword,
                    [ApplicationRoles.Administrator]),
                user.Id);
            roleOnlyCreateResult.Succeeded.Should().BeTrue();

            var roleOnlyUser = await userManager.FindByIdAsync(roleOnlyCreateResult.UserId!.Value.ToString());
            var roleOnlyPrincipal = await signInManager.CreateUserPrincipalAsync(roleOnlyUser!);
            var roleOnlySecurityStamp = roleOnlyUser!.SecurityStamp;
            var roleOnlyUpdateResult = await administration.UpdateUserAsync(
                new UpdateManagedUser(
                    roleOnlyUser.Id,
                    roleOnlyUser.DisplayName,
                    false,
                    [ApplicationRoles.Viewer]),
                user.Id);
            roleOnlyUpdateResult.Succeeded.Should().BeTrue();

            roleOnlyUser = await userManager.FindByIdAsync(roleOnlyUser.Id.ToString());
            roleOnlyUser!.SecurityStamp.Should().NotBe(roleOnlySecurityStamp);
            (await signInManager.ValidateSecurityStampAsync(roleOnlyPrincipal)).Should().BeNull();

            var teamAdministration = scope.ServiceProvider.GetRequiredService<ITeamAdministrationService>();
            var createTeamResult = await teamAdministration.CreateTeamAsync(
                new CreateManagedTeam("  Platform   Support  ", [roleOnlyUser.Id]),
                user.Id);
            createTeamResult.Succeeded.Should().BeTrue();
            var team = await teamAdministration.FindTeamAsync(createTeamResult.TeamId!.Value);
            team.Should().NotBeNull();
            team!.Name.Should().Be("Platform Support");
            team.Members.Select(member => member.UserId).Should().Equal(roleOnlyUser.Id);
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var teamOwnerSubjectId = await database.OwnerSubjects
                .Where(subject => subject.TeamId == team.Id)
                .Select(subject => subject.Id)
                .SingleAsync();
            var assignmentAccess = scope.ServiceProvider.GetRequiredService<IAssignmentAccessEvaluator>();
            (await assignmentAccess.IsAssignedAsync(
                roleOnlyUser.Id,
                teamOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeTrue();

            var duplicateTeamResult = await teamAdministration.CreateTeamAsync(
                new CreateManagedTeam("platform support", []),
                user.Id);
            duplicateTeamResult.Succeeded.Should().BeFalse();

            var disabledTeamResult = await teamAdministration.CreateTeamAsync(
                new CreateManagedTeam("Disabled Team", [roleOnlyUser.Id]),
                user.Id);
            disabledTeamResult.Succeeded.Should().BeTrue();
            var disabledTeam = await teamAdministration.FindTeamAsync(disabledTeamResult.TeamId!.Value);
            var disabledTeamOwnerSubjectId = await database.OwnerSubjects
                .Where(subject => subject.TeamId == disabledTeam!.Id)
                .Select(subject => subject.Id)
                .SingleAsync();
            (await teamAdministration.UpdateTeamAsync(
                new UpdateManagedTeam(
                    disabledTeam!.Id,
                    disabledTeam.Name,
                    true,
                    disabledTeam.Version,
                    [roleOnlyUser.Id]),
                user.Id)).Succeeded.Should().BeTrue();
            (await assignmentAccess.IsAssignedAsync(
                roleOnlyUser.Id,
                disabledTeamOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeFalse();

            var updateTeamResult = await teamAdministration.UpdateTeamAsync(
                new UpdateManagedTeam(team.Id, "Platform Reliability", false, team.Version, []),
                user.Id);
            updateTeamResult.Succeeded.Should().BeTrue();
            var listedTeams = await teamAdministration.ListTeamsAsync();
            listedTeams.Should().ContainSingle(candidate =>
                candidate.Id == team.Id
                && candidate.Name == "Platform Reliability"
                && candidate.Members.Count == 0);
            var staleTeamResult = await teamAdministration.UpdateTeamAsync(
                new UpdateManagedTeam(team.Id, "Stale update", false, team.Version, []),
                user.Id);
            staleTeamResult.Succeeded.Should().BeFalse();

            (await database.OwnerSubjects.CountAsync(subject => subject.TeamId == team.Id)).Should().Be(1);
            (await assignmentAccess.IsAssignedAsync(
                roleOnlyUser.Id,
                teamOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeFalse();
            var closedMembership = await database.TeamMembers.SingleAsync(member => member.TeamId == team.Id);
            closedMembership.EffectiveUntil.Should().NotBeNull();
            await VerifyMembershipPeriodsCannotOverlapAsync(
                connectionString,
                closedMembership,
                user.Id);

            var auditTrail = scope.ServiceProvider.GetRequiredService<IAuditTrailReader>();
            var teamAudit = await auditTrail.SearchAsync(new AuditSearchQuery(
                ActorUserId: user.Id,
                Action: "team.updated",
                Entity: team.Id.ToString()));
            teamAudit.TotalCount.Should().Be(1);
            teamAudit.Events[0].BeforeValues.Should().ContainKey("memberUserIds");
            teamAudit.Events[0].AfterValues.Should().ContainKey("memberUserIds");
            var boundedAuditPage = await auditTrail.SearchAsync(new AuditSearchQuery(
                ToDate: DateOnly.MaxValue,
                Page: int.MaxValue));
            boundedAuditPage.Page.Should().Be(1);
            boundedAuditPage.Events.Should().NotBeEmpty();
            var userAudit = await auditTrail.SearchAsync(new AuditSearchQuery(
                ActorUserId: user.Id,
                Action: "user.updated",
                Entity: managedUser.Id.ToString()));
            userAudit.TotalCount.Should().Be(1);
            userAudit.Events[0].AfterValues["passwordReset"].Should().Be("true");
            userAudit.Events[0].AfterValues.Values.Should().NotContain(replacementPassword);
            (await database.OwnerSubjects.AnyAsync(subject => subject.UserId == roleOnlyUser.Id))
                .Should().BeTrue();
            var userOwnerSubjectId = await database.OwnerSubjects
                .Where(subject => subject.UserId == roleOnlyUser.Id)
                .Select(subject => subject.Id)
                .SingleAsync();
            (await assignmentAccess.IsAssignedAsync(
                roleOnlyUser.Id,
                userOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeTrue();
            var disabledUserOwnerSubjectId = await database.OwnerSubjects
                .Where(subject => subject.UserId == managedUser.Id)
                .Select(subject => subject.Id)
                .SingleAsync();
            (await assignmentAccess.IsAssignedAsync(
                managedUser.Id,
                disabledUserOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeFalse();

            await VerifyDisabledMembershipRetentionAsync(scope.ServiceProvider, user);
            await VerifyConcurrentDisableIsObservedAsync(services, user, connectionString);

            var auditWriter = scope.ServiceProvider.GetRequiredService<IAuthorizationDenialAuditWriter>();
            await auditWriter.WriteAsync(new AuthorizationDenialAuditEntry(
                user.Id,
                DateTimeOffset.UtcNow,
                "GET",
                "/Administration/Users",
                "database-foundation-correlation"));
            var auditEvent = await database
                .AuditEvents
                .SingleAsync(candidate => candidate.Action == "authorization.denied");
            auditEvent.ActorUserId.Should().Be(user.Id);
            auditEvent.Action.Should().Be("authorization.denied");
            auditEvent.EntityIdentifier.Should().Be("/Administration/Users");
            auditEvent.Outcome.Should().Be("forbidden");

            await VerifyAuditRowsAreAppendOnlyAsync(connectionString, auditEvent.Id);
        }
    }

    private static async Task VerifyDisabledMembershipRetentionAsync(
        IServiceProvider services,
        ApplicationUser administrator)
    {
        var userAdministration = services.GetRequiredService<IUserAdministrationService>();
        var teamAdministration = services.GetRequiredService<ITeamAdministrationService>();
        var database = services.GetRequiredService<ApplicationDbContext>();
        var password = $"Retained-5!{Guid.NewGuid():N}";
        var createUser = await userAdministration.CreateUserAsync(
            new CreateManagedUser(
                "Retained Disabled Member",
                "retained-disabled@example.test",
                password,
                [ApplicationRoles.Viewer]),
            administrator.Id);
        createUser.Succeeded.Should().BeTrue();
        var memberId = createUser.UserId!.Value;
        var createTeam = await teamAdministration.CreateTeamAsync(
            new CreateManagedTeam("Retention Team", [memberId]),
            administrator.Id);
        createTeam.Succeeded.Should().BeTrue();

        var disableUser = await userAdministration.UpdateUserAsync(
            new UpdateManagedUser(
                memberId,
                "Retained Disabled Member",
                true,
                [ApplicationRoles.Viewer]),
            administrator.Id);
        disableUser.Succeeded.Should().BeTrue();
        var team = await teamAdministration.FindTeamAsync(createTeam.TeamId!.Value);
        var renameTeam = await teamAdministration.UpdateTeamAsync(
            new UpdateManagedTeam(
                team!.Id,
                "Renamed Retention Team",
                false,
                team.Version,
                [memberId]),
            administrator.Id);
        renameTeam.Succeeded.Should().BeTrue();
        (await database.TeamMembers.SingleAsync(member => member.TeamId == team.Id))
            .EffectiveUntil.Should().BeNull();

        var addDisabledUser = await teamAdministration.CreateTeamAsync(
            new CreateManagedTeam("Rejected Disabled Assignment", [memberId]),
            administrator.Id);
        addDisabledUser.Succeeded.Should().BeFalse();
    }

    private static async Task VerifyConcurrentDisableIsObservedAsync(
        IServiceProvider rootServices,
        ApplicationUser administrator,
        string connectionString)
    {
        Guid createUserId;
        Guid updateUserId;
        await using (var setupScope = rootServices.CreateAsyncScope())
        {
            var userAdministration = setupScope.ServiceProvider
                .GetRequiredService<IUserAdministrationService>();
            var createUser = await userAdministration.CreateUserAsync(
                new CreateManagedUser(
                    "Concurrent Create User",
                    "concurrent-create@example.test",
                    $"Concurrent-4!{Guid.NewGuid():N}",
                    [ApplicationRoles.Viewer]),
                administrator.Id);
            createUser.Succeeded.Should().BeTrue();
            createUserId = createUser.UserId!.Value;
            var updateUser = await userAdministration.CreateUserAsync(
                new CreateManagedUser(
                    "Concurrent Update User",
                    "concurrent-update@example.test",
                    $"Concurrent-3!{Guid.NewGuid():N}",
                    [ApplicationRoles.Viewer]),
                administrator.Id);
            updateUser.Succeeded.Should().BeTrue();
            updateUserId = updateUser.UserId!.Value;
        }

        await using var assignmentScope = rootServices.CreateAsyncScope();
        var teamAdministration = assignmentScope.ServiceProvider
            .GetRequiredService<ITeamAdministrationService>();
        var createResult = await RunDuringConcurrentDisableAsync(
            connectionString,
            createUserId,
            () => teamAdministration.CreateTeamAsync(
                new CreateManagedTeam("Concurrent Create Team", [createUserId]),
                administrator.Id));
        createResult.Succeeded.Should().BeFalse();

        var emptyTeamResult = await teamAdministration.CreateTeamAsync(
            new CreateManagedTeam("Concurrent Update Team", []),
            administrator.Id);
        emptyTeamResult.Succeeded.Should().BeTrue();
        var emptyTeam = await teamAdministration.FindTeamAsync(emptyTeamResult.TeamId!.Value);
        var updateResult = await RunDuringConcurrentDisableAsync(
            connectionString,
            updateUserId,
            () => teamAdministration.UpdateTeamAsync(
                new UpdateManagedTeam(
                    emptyTeam!.Id,
                    emptyTeam.Name,
                    false,
                    emptyTeam.Version,
                    [updateUserId]),
                administrator.Id));
        updateResult.Succeeded.Should().BeFalse();
    }

    private static async Task<TeamAdministrationResult> RunDuringConcurrentDisableAsync(
        string connectionString,
        Guid userId,
        Func<Task<TeamAdministrationResult>> mutation)
    {
        await using var blockingConnection = new NpgsqlConnection(connectionString);
        await blockingConnection.OpenAsync();
        await using var blockingTransaction = await blockingConnection.BeginTransactionAsync();
        await using (var disableCommand = new NpgsqlCommand(
                         "UPDATE web_health.app_user SET is_disabled = TRUE WHERE id = @id",
                         blockingConnection,
                         blockingTransaction))
        {
            disableCommand.Parameters.AddWithValue("id", userId);
            (await disableCommand.ExecuteNonQueryAsync()).Should().Be(1);
        }

        var mutationTask = mutation();
        await Task.Delay(200);
        mutationTask.IsCompleted.Should().BeFalse();

        await blockingTransaction.CommitAsync();
        return await mutationTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task VerifyAuditRowsAreAppendOnlyAsync(
        string connectionString,
        Guid auditEventId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE web_health.audit_event SET outcome = 'changed' WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", auditEventId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.RaiseException);
    }

    private static async Task VerifyMembershipPeriodsCannotOverlapAsync(
        string connectionString,
        TeamMember existingMembership,
        Guid actorUserId)
    {
        const string sql = """
            INSERT INTO web_health.team_member
                (id, team_id, user_id, effective_from, effective_until, created_at, created_by_user_id)
            VALUES
                (@id, @team_id, @user_id, @effective_from, @effective_until, @created_at, @actor_user_id);
            """;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("team_id", existingMembership.TeamId);
        command.Parameters.AddWithValue("user_id", existingMembership.UserId);
        command.Parameters.AddWithValue("effective_from", existingMembership.EffectiveFrom);
        command.Parameters.AddWithValue("effective_until", existingMembership.EffectiveUntil!.Value);
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
    }

    private static async Task<(bool SchemaExists, IReadOnlyList<string> Tables)> ReadFoundationState(
        string connectionString)
    {
        const string sql = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'web_health'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return (tables.Count > 0, tables);
    }
}
