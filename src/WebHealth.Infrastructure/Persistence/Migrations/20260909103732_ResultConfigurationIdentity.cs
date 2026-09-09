using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    public partial class ResultConfigurationIdentity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "configuration_identity",
                schema: "web_health",
                table: "check_result",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE web_health.check_result AS result
                SET configuration_identity =
                    snapshot.configuration_fingerprint || ':' ||
                    snapshot.schema_version::text || ':' ||
                    coalesce(snapshot.current_truth_generation::text, '') || E'\n'
                FROM web_health.check_configuration_snapshot AS snapshot
                WHERE snapshot.logical_check_id = result.logical_check_id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "configuration_identity",
                schema: "web_health",
                table: "check_result",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_result_configuration_identity",
                schema: "web_health",
                table: "check_result",
                sql: "configuration_identity <> ''");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_check_result_configuration_identity",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DropColumn(
                name: "configuration_identity",
                schema: "web_health",
                table: "check_result");
        }
    }
}
