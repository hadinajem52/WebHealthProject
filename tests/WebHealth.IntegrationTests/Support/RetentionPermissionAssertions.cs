using FluentAssertions;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence;
using Xunit;

namespace WebHealth.IntegrationTests.Support;

internal static class RetentionPermissionAssertions
{
    public static async Task VerifyUpgradeAsync(ApplicationDbContext database, Guid logicalCheckId)
    {
        var connectionString = database.Database.GetConnectionString()!;
        await database.Database.MigrateAsync("MonitoringDailyAggregates");
        await VerifyAsync(connectionString, "check_configuration_snapshot", "logical_check_id", logicalCheckId, false);
        await database.Database.MigrateAsync();
        await VerifyAsync(connectionString, "check_configuration_snapshot", "logical_check_id", logicalCheckId);
        await database.Database.MigrateAsync();
        await VerifyAsync(connectionString, "check_configuration_snapshot", "logical_check_id", logicalCheckId);
    }

    public static async Task VerifyAsync(string connectionString, string table, string key, Guid id, bool allowsDelete = true)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await transaction.SaveAsync("baseline");
            await using var delete = new NpgsqlCommand($"DELETE FROM web_health.{table} WHERE {key} = @id", connection, transaction);
            delete.Parameters.AddWithValue("id", id);
            (await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync()))
                .SqlState.Should().Be(PostgresErrorCodes.RaiseException);
            await transaction.RollbackAsync("baseline");
            await using var enable = new NpgsqlCommand("SET LOCAL web_health.monitoring_retention = 'on'", connection, transaction);
            await enable.ExecuteNonQueryAsync();
            await transaction.SaveAsync("enabled");
            await using var update = new NpgsqlCommand($"UPDATE web_health.{table} SET {key} = {key} WHERE {key} = @id", connection, transaction);
            update.Parameters.AddWithValue("id", id);
            (await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync()))
                .SqlState.Should().Be(PostgresErrorCodes.RaiseException);
            await transaction.RollbackAsync("enabled");
            if (allowsDelete) (await delete.ExecuteNonQueryAsync()).Should().Be(1);
            else
            {
                (await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync()))
                    .SqlState.Should().Be(PostgresErrorCodes.RaiseException);
                await transaction.RollbackAsync("enabled");
            }
            await transaction.RollbackAsync();
        }
        await AssertPermissionResetAsync(connection);
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await using var enable = new NpgsqlCommand("SET LOCAL web_health.monitoring_retention = 'on'", connection, transaction);
            await enable.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        await AssertPermissionResetAsync(connection);
    }

    private static async Task AssertPermissionResetAsync(NpgsqlConnection connection)
    {
        await using var read = new NpgsqlCommand("SELECT coalesce(current_setting('web_health.monitoring_retention', TRUE), '')", connection);
        (await read.ExecuteScalarAsync()).Should().NotBe("on");
    }
}
