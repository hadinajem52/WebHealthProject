using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeoValueExtraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "seo_observation",
                schema: "web_health",
                columns: table => new
                {
                    logical_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applicability = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    not_applicable_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    document_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    title_length = table.Column<int>(type: "integer", nullable: false),
                    title_count = table.Column<int>(type: "integer", nullable: false),
                    meta_description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    meta_description_length = table.Column<int>(type: "integer", nullable: false),
                    meta_description_count = table.Column<int>(type: "integer", nullable: false),
                    canonical_href = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    canonical_length = table.Column<int>(type: "integer", nullable: false),
                    canonical_count = table.Column<int>(type: "integer", nullable: false),
                    canonical_absolute_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    robots_meta = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    robots_meta_length = table.Column<int>(type: "integer", nullable: false),
                    robots_meta_count = table.Column<int>(type: "integer", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_observation", x => x.logical_check_id);
                    table.CheckConstraint("ck_seo_observation_applicability", "applicability IN ('Applicable', 'NotApplicable')");
                    table.CheckConstraint("ck_seo_observation_applicability_fields", "(applicability = 'Applicable' AND not_applicable_reason IS NULL) OR (applicability = 'NotApplicable' AND not_applicable_reason IN ('TransportFailed', 'NonSuccessStatus', 'NonHtml', 'EmptyBody', 'ExtractionFailed') AND title IS NULL AND meta_description IS NULL AND canonical_href IS NULL AND canonical_absolute_url IS NULL AND robots_meta IS NULL AND title_count = 0 AND meta_description_count = 0 AND canonical_count = 0 AND robots_meta_count = 0 AND title_length = 0 AND meta_description_length = 0 AND canonical_length = 0 AND robots_meta_length = 0)");
                    table.CheckConstraint("ck_seo_observation_counts", "title_count >= 0 AND meta_description_count >= 0 AND canonical_count >= 0 AND robots_meta_count >= 0");
                    table.CheckConstraint("ck_seo_observation_lengths", "title_length >= COALESCE(length(title), 0) AND meta_description_length >= COALESCE(length(meta_description), 0) AND canonical_length >= COALESCE(length(canonical_href), 0) AND robots_meta_length >= COALESCE(length(robots_meta), 0)");
                    table.ForeignKey(
                        name: "fk_seo_observation_endpoint_monitor_endpoint_monitor_id",
                        column: x => x.endpoint_monitor_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_seo_observation_logical_check_logical_check_id_endpoint_mon~",
                        columns: x => new { x.logical_check_id, x.endpoint_monitor_id },
                        principalSchema: "web_health",
                        principalTable: "logical_check",
                        principalColumns: new[] { "id", "endpoint_monitor_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_seo_observation_endpoint_monitor_id_observed_at",
                schema: "web_health",
                table: "seo_observation",
                columns: new[] { "endpoint_monitor_id", "observed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_seo_observation_logical_check_id_endpoint_monitor_id",
                schema: "web_health",
                table: "seo_observation",
                columns: new[] { "logical_check_id", "endpoint_monitor_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "seo_observation",
                schema: "web_health");
        }
    }
}
