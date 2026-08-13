using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Infrastructure.Diagnostics;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "WebHealth";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
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

        services.AddHealthChecks()
            .AddCheck<PostgreSqlReadinessCheck>("postgresql", tags: ["ready"]);

        return services;
    }
}
