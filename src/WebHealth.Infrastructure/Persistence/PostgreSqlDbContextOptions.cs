using Microsoft.EntityFrameworkCore;

namespace WebHealth.Infrastructure.Persistence;

internal static class PostgreSqlDbContextOptions
{
    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly);
            npgsqlOptions.MigrationsHistoryTable(
                DatabaseConventions.MigrationsHistoryTable,
                DatabaseConventions.DefaultSchema);
        });
    }
}
