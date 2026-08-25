using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence.CompiledModels;

namespace WebHealth.Infrastructure.Persistence;

internal static class PostgreSqlDbContextOptions
{
    public static void Configure(
        DbContextOptionsBuilder options,
        string connectionString,
        bool useCompiledModel = true)
    {
        options.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly);
            npgsqlOptions.MigrationsHistoryTable(
                DatabaseConventions.MigrationsHistoryTable,
                DatabaseConventions.DefaultSchema);
        });

        if (useCompiledModel)
        {
            options.UseModel(ApplicationDbContextModel.Instance);
        }
    }
}
