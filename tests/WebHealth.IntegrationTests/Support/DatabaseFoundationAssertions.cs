using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.IntegrationTests.Support;

internal static class DatabaseFoundationAssertions
{
    public static async Task VerifyAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString);
        await using var context = new ApplicationDbContext(options.Options);

        await context.Database.MigrateAsync();

        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await context.Database.GetAppliedMigrationsAsync()).Should().ContainSingle();
        context.Model.GetEntityTypes().Should().BeEmpty();
        (await ReadFoundationState(connectionString)).Should().Be((true, 1L));
    }

    private static async Task<(bool SchemaExists, long TableCount)> ReadFoundationState(
        string connectionString)
    {
        const string sql = """
            SELECT
                to_regnamespace('web_health') IS NOT NULL,
                count(*)
            FROM information_schema.tables
            WHERE table_schema = 'web_health'
              AND table_type = 'BASE TABLE';
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetBoolean(0), reader.GetInt64(1));
    }
}
