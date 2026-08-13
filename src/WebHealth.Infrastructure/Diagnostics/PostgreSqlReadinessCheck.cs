using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace WebHealth.Infrastructure.Diagnostics;

internal sealed class PostgreSqlReadinessCheck(
    IConfiguration configuration,
    ILogger<PostgreSqlReadinessCheck> logger) : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString(DependencyInjection.DatabaseConnectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy("PostgreSQL configuration is unavailable.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("PostgreSQL readiness check timed out.");
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "PostgreSQL readiness check failed with {ExceptionType}.",
                exception.GetType().Name);
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
        }
    }
}
