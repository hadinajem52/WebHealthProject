using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.IntegrationTests.Support;

internal static class RetentionHoldAssertions
{
    public static async Task VerifyManagementAsync(string connectionString, Guid monitorId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString);
        await using var database = new ApplicationDbContext(options.Options);
        var monitor = await database.EndpointMonitors.Include(item => item.Endpoint).SingleAsync(item => item.Id == monitorId);
        var actor = monitor.Endpoint.CreatedByUserId;
        var admin = new RegistryAccessContext(actor, [ApplicationRoles.Administrator]);
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var clock = new HoldTimeProvider(now);
        var service = new RetentionHoldService(database, clock);
        var command = new CreateRetentionHold("Endpoint", monitor.EndpointId, "  Controlled hold  ", null);
        foreach (var role in new[] { ApplicationRoles.Operations, ApplicationRoles.DeveloperSupport, ApplicationRoles.Viewer })
        {
            var denied = new RegistryAccessContext(actor, [role]);
            (await service.CreateAsync(command, denied)).Status.Should().Be(RegistryMutationStatus.Forbidden);
            (await service.ReleaseAsync(Guid.NewGuid(), denied)).Status.Should().Be(RegistryMutationStatus.Forbidden);
            var read = async () => await service.ListAsync(denied);
            await read.Should().ThrowAsync<UnauthorizedAccessException>();
        }
        (await service.CreateAsync(command, new(Guid.NewGuid(), [ApplicationRoles.Administrator])))
            .Status.Should().Be(RegistryMutationStatus.Forbidden);
        foreach (var invalid in new[]
        {
            command with { ScopeType = "Unknown" }, command with { ScopeId = Guid.NewGuid() },
            command with { Reason = " " }, command with { Reason = new string('x', 501) },
            command with { ExpiresAt = now }, command with { ExpiresAt = now.AddTicks(1) }
        })
            (await service.CreateAsync(invalid, admin)).Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var created = await service.CreateAsync(command with { ExpiresAt = now.AddTicks(10) }, admin);
        created.Succeeded.Should().BeTrue(string.Join(" ", created.Errors));
        var hold = await database.RetentionHolds.AsNoTracking().SingleAsync(item => item.Id == created.EntityId);
        hold.Reason.Should().Be("Controlled hold");
        hold.ExpiresAt.Should().Be(now.AddTicks(10));
        (await service.ListAsync(admin)).Should().Contain(item => item.Id == hold.Id);
        await using var otherDatabase = new ApplicationDbContext(options.Options);
        var otherService = new RetentionHoldService(otherDatabase, clock);
        var releases = await Task.WhenAll(service.ReleaseAsync(hold.Id, admin), otherService.ReleaseAsync(hold.Id, admin));
        releases.Should().AllSatisfy(result => result.Succeeded.Should().BeTrue(string.Join(" ", result.Errors)));
        hold = await database.RetentionHolds.AsNoTracking().SingleAsync(item => item.Id == hold.Id);
        hold.ReleasedAt.Should().Be(now);
        hold.ReleasedByUserId.Should().Be(actor);
        var audits = await database.AuditEvents.AsNoTracking().Where(item => item.EntityType == "retention_hold"
            && item.EntityIdentifier == hold.Id.ToString()).ToArrayAsync();
        audits.Select(item => item.Action).Should().BeEquivalentTo("retention.holdcreated", "retention.holdreleased");
        audits.Should().OnlyContain(item => item.ActorUserId == actor && item.OccurredAt == now);
        audits.Should().OnlyContain(item => !item.AfterValues!.Contains("Controlled hold"));
        (await service.ReleaseAsync(Guid.NewGuid(), admin)).Status.Should().Be(RegistryMutationStatus.NotFound);
    }

    public static async Task VerifyAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString);
        await using var database = new ApplicationDbContext(options.Options);
        var columns = await database.Database.SqlQueryRaw<string>("""
            SELECT column_name AS "Value" FROM information_schema.columns
            WHERE table_schema = 'web_health' AND table_name = 'retention_hold'
            """).ToArrayAsync();
        columns.Should().BeEquivalentTo("id", "scope_type", "scope_id", "reason", "created_by_user_id",
            "created_at", "expires_at", "released_by_user_id", "released_at");
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        foreach (var scope in new[] { "Client", "Website", "Environment", "Endpoint", "Monitor", "LogicalCheck", "Incident", "CrawlRun", "PageAuditRun" })
        {
            var hold = NewHold(scope, now);
            database.RetentionHolds.Add(hold);
            await database.SaveChangesAsync();
            var saved = await database.RetentionHolds.AsNoTracking().SingleAsync(item => item.Id == hold.Id);
            saved.CreatedAt.Should().Be(now);
            saved.ExpiresAt.Should().BeNull();
            foreach (var mutation in new[]
            {
                "scope_type = 'Unsupported'", "scope_id = '00000000-0000-0000-0000-000000000000'",
                "reason = ' '", "created_by_user_id = '00000000-0000-0000-0000-000000000000'",
                "expires_at = created_at", "released_at = created_at", "released_by_user_id = created_by_user_id"
            })
            {
                var statement = "UPDATE web_health.retention_hold SET " + mutation + " WHERE id = {0}";
                var invalid = async () => await database.Database.ExecuteSqlRawAsync(statement, hold.Id);
                (await invalid.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            }
            hold.ExpiresAt = now.AddTicks(10);
            hold.ReleasedAt = now;
            hold.ReleasedByUserId = hold.CreatedByUserId;
            await database.SaveChangesAsync();
            saved = await database.RetentionHolds.AsNoTracking().SingleAsync(item => item.Id == hold.Id);
            saved.ExpiresAt.Should().Be(now.AddTicks(10));
            saved.ReleasedAt.Should().Be(now);
            database.RetentionHolds.Remove(hold);
            await database.SaveChangesAsync();
        }
    }

    public static async Task VerifyUpgradeAsync(ApplicationDbContext database)
    {
        database.RetentionHolds.Add(NewHold("Endpoint", DateTimeOffset.UtcNow));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        await database.Database.MigrateAsync("MonitoringRuntimeState");
        await database.Database.MigrateAsync();
        (await database.RetentionHolds.CountAsync()).Should().Be(0, "rollback drops holds and upgrade must not invent them");
        var hold = NewHold("Endpoint", DateTimeOffset.UtcNow);
        database.RetentionHolds.Add(hold);
        await database.SaveChangesAsync();
        await database.Database.MigrateAsync();
        (await database.RetentionHolds.SingleAsync()).Id.Should().Be(hold.Id);
        database.RetentionHolds.Remove(hold);
        await database.SaveChangesAsync();
    }

    private sealed class HoldTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static RetentionHold NewHold(string scope, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ScopeType = scope,
        ScopeId = Guid.NewGuid(),
        Reason = "Controlled retention hold",
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = now
    };
}
