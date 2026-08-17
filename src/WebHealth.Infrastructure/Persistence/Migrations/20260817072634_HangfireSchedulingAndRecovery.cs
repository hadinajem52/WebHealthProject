using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HangfireSchedulingAndRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_durable_work_state",
                schema: "web_health",
                table: "durable_work");

            migrationBuilder.AddCheckConstraint(
                name: "ck_durable_work_state",
                schema: "web_health",
                table: "durable_work",
                sql: "state IN ('Pending', 'Dispatching', 'Enqueued', 'Processing', 'Completed', 'Failed')");

            foreach (var sql in LoadHangfireInstallScripts())
            {
                migrationBuilder.Sql(sql);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_durable_work_state",
                schema: "web_health",
                table: "durable_work");

            migrationBuilder.Sql(
                "UPDATE web_health.durable_work SET state = 'Pending' WHERE state = 'Dispatching'");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS hangfire CASCADE");

            migrationBuilder.AddCheckConstraint(
                name: "ck_durable_work_state",
                schema: "web_health",
                table: "durable_work",
                sql: "state IN ('Pending', 'Enqueued', 'Processing', 'Completed', 'Failed')");
        }

        private static IEnumerable<string> LoadHangfireInstallScripts()
        {
            var assembly = typeof(Hangfire.PostgreSql.PostgreSqlStorage).Assembly;
            for (var version = 3; version <= 23; version++)
            {
                var resourceName = $"Hangfire.PostgreSql.Scripts.Install.v{version}.sql";
                using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException($"Missing Hangfire schema resource {resourceName}.");
                using var reader = new StreamReader(stream);
                yield return reader.ReadToEnd();
            }
        }
    }
}
