using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    public partial class StructuredCertificateFacts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "chain_status_codes",
                schema: "web_health",
                table: "certificate_observation",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "chain_trust_status",
                schema: "web_health",
                table: "certificate_observation",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "hostname_status",
                schema: "web_health",
                table: "certificate_observation",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "validity_status",
                schema: "web_health",
                table: "certificate_observation",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Valid");

            migrationBuilder.Sql("""
                UPDATE web_health.certificate_observation
                SET validity_status = CASE WHEN observed_at < not_before THEN 'NotYetValid'
                        WHEN observed_at > not_after THEN 'Expired' ELSE 'Valid' END,
                    hostname_status = CASE WHEN hostname_matched THEN 'Matched' ELSE 'Mismatched' END,
                    chain_trust_status = CASE WHEN NOT chain_trusted THEN 'Untrusted' ELSE 'Unknown' END;
                ALTER TABLE web_health.certificate_observation
                    ALTER COLUMN validity_status DROP DEFAULT,
                    ALTER COLUMN hostname_status DROP DEFAULT,
                    ALTER COLUMN chain_trust_status DROP DEFAULT,
                    ALTER COLUMN chain_status_codes DROP DEFAULT;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_certificate_observation_chain_codes",
                schema: "web_health",
                table: "certificate_observation",
                sql: "jsonb_typeof(chain_status_codes) = 'array' AND jsonb_array_length(chain_status_codes) <= 32");

            migrationBuilder.AddCheckConstraint(
                name: "ck_certificate_observation_structured_status",
                schema: "web_health",
                table: "certificate_observation",
                sql: "validity_status IN ('Valid','Expired','NotYetValid') AND hostname_status IN ('Matched','Mismatched','Unknown') AND chain_trust_status IN ('Trusted','Untrusted','Unknown')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_certificate_observation_chain_codes",
                schema: "web_health",
                table: "certificate_observation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_certificate_observation_structured_status",
                schema: "web_health",
                table: "certificate_observation");

            migrationBuilder.DropColumn(
                name: "chain_status_codes",
                schema: "web_health",
                table: "certificate_observation");

            migrationBuilder.DropColumn(
                name: "chain_trust_status",
                schema: "web_health",
                table: "certificate_observation");

            migrationBuilder.DropColumn(
                name: "hostname_status",
                schema: "web_health",
                table: "certificate_observation");

            migrationBuilder.DropColumn(
                name: "validity_status",
                schema: "web_health",
                table: "certificate_observation");
        }
    }
}
