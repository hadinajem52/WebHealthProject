using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.IntegrationTests.Support;

internal static class RetentionHoldAssertions
{
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
