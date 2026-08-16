using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class ExecutionLeaseService(ApplicationDbContext dbContext) : IExecutionLeaseService
{
    private static readonly TimeSpan MaximumLeaseDuration = TimeSpan.FromMinutes(15);

    public async Task<ExecutionLeaseClaim?> TryAcquireAsync(
        AcquireExecutionLease request,
        CancellationToken cancellationToken = default)
    {
        if (request.LeaseDuration <= TimeSpan.Zero || request.LeaseDuration > MaximumLeaseDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"Lease duration must be positive and no longer than {MaximumLeaseDuration}.");
        }

        const string sql = """
            WITH lease_time AS (
                SELECT clock_timestamp() AS acquired_at
            )
            INSERT INTO web_health.execution_lease
                (endpoint_monitor_id, logical_check_id, owner_token, fencing_generation, acquired_at, expires_at)
            SELECT @endpoint_monitor_id, @logical_check_id, @owner_token, 1,
                   acquired_at, acquired_at + @lease_duration
            FROM lease_time
            ON CONFLICT (endpoint_monitor_id) DO UPDATE
            SET logical_check_id = EXCLUDED.logical_check_id,
                owner_token = EXCLUDED.owner_token,
                fencing_generation = web_health.execution_lease.fencing_generation + 1,
                acquired_at = EXCLUDED.acquired_at,
                expires_at = EXCLUDED.expires_at
            WHERE web_health.execution_lease.expires_at <= EXCLUDED.acquired_at
            RETURNING fencing_generation;
            """;

        var generation = await ExecuteScalarAsync(sql, request, cancellationToken);
        return generation is null
            ? null
            : new(
                request.EndpointMonitorId,
                request.LogicalCheckId,
                request.OwnerToken,
                generation.Value);
    }

    public async Task<bool> ReleaseAsync(
        ExecutionLeaseClaim claim,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE web_health.execution_lease
            SET expires_at = GREATEST(clock_timestamp(), acquired_at + interval '1 microsecond')
            WHERE endpoint_monitor_id = @endpoint_monitor_id
              AND logical_check_id = @logical_check_id
              AND owner_token = @owner_token
              AND fencing_generation = @fencing_generation
              AND expires_at > clock_timestamp();
            """;

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, CurrentTransaction());
            command.Parameters.AddWithValue("endpoint_monitor_id", NpgsqlDbType.Uuid, claim.EndpointMonitorId);
            command.Parameters.AddWithValue("logical_check_id", NpgsqlDbType.Uuid, claim.LogicalCheckId);
            command.Parameters.AddWithValue("owner_token", NpgsqlDbType.Uuid, claim.OwnerToken);
            command.Parameters.AddWithValue("fencing_generation", NpgsqlDbType.Bigint, claim.FencingGeneration);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<long?> ExecuteScalarAsync(
        string sql,
        AcquireExecutionLease request,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, CurrentTransaction());
            command.Parameters.AddWithValue("endpoint_monitor_id", NpgsqlDbType.Uuid, request.EndpointMonitorId);
            command.Parameters.AddWithValue("logical_check_id", NpgsqlDbType.Uuid, request.LogicalCheckId);
            command.Parameters.AddWithValue("owner_token", NpgsqlDbType.Uuid, request.OwnerToken);
            command.Parameters.AddWithValue("lease_duration", NpgsqlDbType.Interval, request.LeaseDuration);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is long generation ? generation : null;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private NpgsqlTransaction? CurrentTransaction() =>
        dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
}
