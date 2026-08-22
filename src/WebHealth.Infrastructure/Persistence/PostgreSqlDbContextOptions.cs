using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence.CompiledModels;

namespace WebHealth.Infrastructure.Persistence;

internal static class PostgreSqlDbContextOptions
{
    /// <summary>
    /// Builds the options every <see cref="ApplicationDbContext" /> is constructed from.
    /// </summary>
    /// <param name="useCompiledModel">
    /// Whether to bind the pre-built model. On by default, because discovering this model from
    /// its configuration is the single largest cost of the first query in a process — measured at
    /// roughly six seconds between the host starting and the first statement reaching PostgreSQL,
    /// which is most of what a person waits through when they sign in after a restart.
    /// <para>
    /// Design-time tooling must pass <see langword="false" />. <c>migrations add</c> and
    /// <c>has-pending-model-changes</c> exist to compare the configuration against the last
    /// migration, and handing them a model built from a previous run would have them compare that
    /// snapshot with itself — reporting no pending changes for a model that had in fact changed.
    /// </para>
    /// </param>
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
