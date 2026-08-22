using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WebHealth.Infrastructure.Persistence;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public const string ConnectionStringEnvironmentVariable = "WEBHEALTH_MIGRATIONS_CONNECTION";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringEnvironmentVariable} before running Entity Framework commands.");
        }

        // Built from the configuration rather than from the compiled model: this context exists
        // for the tooling that compares the two, and that comparison is meaningless if both
        // sides come from the same generated snapshot.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString, useCompiledModel: false);
        return new ApplicationDbContext(options.Options);
    }
}
