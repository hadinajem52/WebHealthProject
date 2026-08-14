using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RegistryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalization_version = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_app_user_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_app_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_owner_subject_owner_subject_id",
                        column: x => x.owner_subject_id,
                        principalSchema: "web_health",
                        principalTable: "owner_subject",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "policy_profile",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    monitor_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    bounded_settings = table.Column<string>(type: "jsonb", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_policy_profile", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tag",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalization_version = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tag", x => x.id);
                    table.ForeignKey(
                        name: "fk_tag_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "website",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalization_version = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    technology_cms = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_website", x => x.id);
                    table.ForeignKey(
                        name: "fk_website_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_app_user_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_app_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_client_client_id",
                        column: x => x.client_id,
                        principalSchema: "web_health",
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_owner_subject_owner_subject_id",
                        column: x => x.owner_subject_id,
                        principalSchema: "web_health",
                        principalTable: "owner_subject",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "environment",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    website_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalization_version = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    environment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_production = table.Column<bool>(type: "boolean", nullable: false),
                    base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_environment", x => x.id);
                    table.CheckConstraint("ck_environment_type_matches_production", "(environment_type = 'Production') = is_production");
                    table.ForeignKey(
                        name: "fk_environment_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_environment_app_user_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_environment_app_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_environment_website_website_id",
                        column: x => x.website_id,
                        principalSchema: "web_health",
                        principalTable: "website",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "website_tag",
                schema: "web_health",
                columns: table => new
                {
                    website_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_website_tag", x => new { x.website_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_website_tag_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_tag_tag_tag_id",
                        column: x => x.tag_id,
                        principalSchema: "web_health",
                        principalTable: "tag",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_website_tag_website_website_id",
                        column: x => x.website_id,
                        principalSchema: "web_health",
                        principalTable: "website",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "endpoint",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    normalized_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    normalized_url_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    normalized_host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    effective_port = table.Column<int>(type: "integer", nullable: false),
                    normalization_version = table.Column<short>(type: "smallint", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    http_exception_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    http_exception_approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    http_exception_approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_endpoint", x => x.id);
                    table.CheckConstraint("ck_endpoint_effective_port", "effective_port BETWEEN 1 AND 65535");
                    table.CheckConstraint("ck_endpoint_http_exception_complete", "(http_exception_reason IS NULL AND http_exception_approved_by_user_id IS NULL AND http_exception_approved_at IS NULL) OR (http_exception_reason IS NOT NULL AND http_exception_approved_by_user_id IS NOT NULL AND http_exception_approved_at IS NOT NULL)");
                    table.CheckConstraint("ck_endpoint_normalized_host", "length(normalized_host) > 0");
                    table.CheckConstraint("ck_endpoint_normalized_scheme", "normalized_url LIKE 'http://%' OR normalized_url LIKE 'https://%'");
                    table.CheckConstraint("ck_endpoint_url_hash_length", "octet_length(normalized_url_hash) = 32");
                    table.ForeignKey(
                        name: "fk_endpoint_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_app_user_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_app_user_http_exception_approved_by_user_id",
                        column: x => x.http_exception_approved_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_app_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_environment_environment_id",
                        column: x => x.environment_id,
                        principalSchema: "web_health",
                        principalTable: "environment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_owner_subject_owner_subject_id",
                        column: x => x.owner_subject_id,
                        principalSchema: "web_health",
                        principalTable: "owner_subject",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "access_grant",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    website_id = table.Column<Guid>(type: "uuid", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_grant", x => x.id);
                    table.CheckConstraint("ck_access_grant_access_level", "access_level IN ('Read', 'Manage')");
                    table.CheckConstraint("ck_access_grant_exactly_one_scope", "(client_id IS NOT NULL)::int + (website_id IS NOT NULL)::int + (environment_id IS NOT NULL)::int + (endpoint_id IS NOT NULL)::int = 1");
                    table.CheckConstraint("ck_access_grant_expiry", "expires_at IS NULL OR expires_at > effective_from");
                    table.ForeignKey(
                        name: "fk_access_grant_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_app_user_revoked_by_user_id",
                        column: x => x.revoked_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_client_client_id",
                        column: x => x.client_id,
                        principalSchema: "web_health",
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_endpoint_endpoint_id",
                        column: x => x.endpoint_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_environment_environment_id",
                        column: x => x.environment_id,
                        principalSchema: "web_health",
                        principalTable: "environment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_access_grant_website_website_id",
                        column: x => x.website_id,
                        principalSchema: "web_health",
                        principalTable: "website",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "endpoint_monitor",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    bounded_overrides = table.Column<string>(type: "jsonb", nullable: false),
                    schedule_anchor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    configuration_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    failure_confirmation_count = table.Column<int>(type: "integer", nullable: false),
                    recovery_confirmation_count = table.Column<int>(type: "integer", nullable: false),
                    warning_threshold_ms = table.Column<int>(type: "integer", nullable: true),
                    critical_threshold_ms = table.Column<int>(type: "integer", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_endpoint_monitor", x => x.id);
                    table.CheckConstraint("ck_endpoint_monitor_positive_confirmation", "failure_confirmation_count > 0 AND recovery_confirmation_count > 0");
                    table.CheckConstraint("ck_endpoint_monitor_positive_interval", "interval_seconds > 0");
                    table.CheckConstraint("ck_endpoint_monitor_positive_timeout", "timeout_seconds > 0");
                    table.CheckConstraint("ck_endpoint_monitor_threshold_order", "warning_threshold_ms IS NULL OR critical_threshold_ms IS NULL OR warning_threshold_ms < critical_threshold_ms");
                    table.ForeignKey(
                        name: "fk_endpoint_monitor_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_monitor_app_user_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_monitor_app_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_monitor_endpoint_endpoint_id",
                        column: x => x.endpoint_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_endpoint_monitor_policy_profile_policy_profile_id",
                        column: x => x.policy_profile_id,
                        principalSchema: "web_health",
                        principalTable: "policy_profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "target_authorization",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    authorization_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    normalized_host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_target_authorization", x => x.id);
                    table.CheckConstraint("ck_target_authorization_expiry", "expires_at IS NULL OR expires_at > effective_from");
                    table.CheckConstraint("ck_target_authorization_kind", "authorization_kind IN ('Owned', 'ExplicitPermission')");
                    table.CheckConstraint("ck_target_authorization_port", "port BETWEEN 1 AND 65535");
                    table.ForeignKey(
                        name: "fk_target_authorization_app_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_target_authorization_app_user_revoked_by_user_id",
                        column: x => x.revoked_by_user_id,
                        principalSchema: "web_health",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_target_authorization_endpoint_endpoint_id",
                        column: x => x.endpoint_id,
                        principalSchema: "web_health",
                        principalTable: "endpoint",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "web_health",
                table: "policy_profile",
                columns: new[] { "id", "bounded_settings", "created_at", "deleted_at", "is_system", "monitor_type", "name", "version" },
                values: new object[] { new Guid("fd3c8021-ff54-4f31-a3ad-2010b7b193dd"), "{}", new DateTimeOffset(new DateTime(2026, 8, 14, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "HttpAvailability", "Default HTTP availability", 1L });

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_client_id",
                schema: "web_health",
                table: "access_grant",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_created_by_user_id",
                schema: "web_health",
                table: "access_grant",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_endpoint_id",
                schema: "web_health",
                table: "access_grant",
                column: "endpoint_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_environment_id",
                schema: "web_health",
                table: "access_grant",
                column: "environment_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_revoked_by_user_id",
                schema: "web_health",
                table: "access_grant",
                column: "revoked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_user_id_client_id_effective_from",
                schema: "web_health",
                table: "access_grant",
                columns: new[] { "user_id", "client_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_user_id_endpoint_id_effective_from",
                schema: "web_health",
                table: "access_grant",
                columns: new[] { "user_id", "endpoint_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_user_id_environment_id_effective_from",
                schema: "web_health",
                table: "access_grant",
                columns: new[] { "user_id", "environment_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_user_id_website_id_effective_from",
                schema: "web_health",
                table: "access_grant",
                columns: new[] { "user_id", "website_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_access_grant_website_id",
                schema: "web_health",
                table: "access_grant",
                column: "website_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_created_by_user_id",
                schema: "web_health",
                table: "client",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_deleted_at_is_active_name",
                schema: "web_health",
                table: "client",
                columns: new[] { "deleted_at", "is_active", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_client_deleted_by_user_id",
                schema: "web_health",
                table: "client",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_normalized_name_normalization_version",
                schema: "web_health",
                table: "client",
                columns: new[] { "normalized_name", "normalization_version" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_client_owner_subject_id",
                schema: "web_health",
                table: "client",
                column: "owner_subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_updated_by_user_id",
                schema: "web_health",
                table: "client",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_created_by_user_id",
                schema: "web_health",
                table: "endpoint",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_deleted_by_user_id",
                schema: "web_health",
                table: "endpoint",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_environment_id_deleted_at_is_enabled",
                schema: "web_health",
                table: "endpoint",
                columns: new[] { "environment_id", "deleted_at", "is_enabled" });

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_http_exception_approved_by_user_id",
                schema: "web_health",
                table: "endpoint",
                column: "http_exception_approved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_owner_subject_id",
                schema: "web_health",
                table: "endpoint",
                column: "owner_subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_updated_by_user_id",
                schema: "web_health",
                table: "endpoint",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_endpoint_environment_url_hash_version_active",
                schema: "web_health",
                table: "endpoint",
                columns: new[] { "environment_id", "normalized_url_hash", "normalization_version" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_monitor_created_by_user_id",
                schema: "web_health",
                table: "endpoint_monitor",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_monitor_deleted_by_user_id",
                schema: "web_health",
                table: "endpoint_monitor",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_monitor_endpoint_id_monitor_type",
                schema: "web_health",
                table: "endpoint_monitor",
                columns: new[] { "endpoint_id", "monitor_type" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_monitor_policy_profile_id",
                schema: "web_health",
                table: "endpoint_monitor",
                column: "policy_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_endpoint_monitor_updated_by_user_id",
                schema: "web_health",
                table: "endpoint_monitor",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_environment_created_by_user_id",
                schema: "web_health",
                table: "environment",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_environment_deleted_by_user_id",
                schema: "web_health",
                table: "environment",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_environment_updated_by_user_id",
                schema: "web_health",
                table: "environment",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_environment_website_id_deleted_at_is_active",
                schema: "web_health",
                table: "environment",
                columns: new[] { "website_id", "deleted_at", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_environment_website_id_normalized_name_normalization_version",
                schema: "web_health",
                table: "environment",
                columns: new[] { "website_id", "normalized_name", "normalization_version" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_policy_profile_name_monitor_type",
                schema: "web_health",
                table: "policy_profile",
                columns: new[] { "name", "monitor_type" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tag_created_by_user_id",
                schema: "web_health",
                table: "tag",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tag_normalized_name_normalization_version",
                schema: "web_health",
                table: "tag",
                columns: new[] { "normalized_name", "normalization_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_target_authorization_created_by_user_id",
                schema: "web_health",
                table: "target_authorization",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_target_authorization_endpoint_id_effective_from_expires_at",
                schema: "web_health",
                table: "target_authorization",
                columns: new[] { "endpoint_id", "effective_from", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_target_authorization_endpoint_id_normalized_host_port",
                schema: "web_health",
                table: "target_authorization",
                columns: new[] { "endpoint_id", "normalized_host", "port" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_target_authorization_revoked_by_user_id",
                schema: "web_health",
                table: "target_authorization",
                column: "revoked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_client_id_deleted_at_is_enabled",
                schema: "web_health",
                table: "website",
                columns: new[] { "client_id", "deleted_at", "is_enabled" });

            migrationBuilder.CreateIndex(
                name: "ix_website_client_id_normalized_name_normalization_version",
                schema: "web_health",
                table: "website",
                columns: new[] { "client_id", "normalized_name", "normalization_version" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_website_created_by_user_id",
                schema: "web_health",
                table: "website",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_deleted_by_user_id",
                schema: "web_health",
                table: "website",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_owner_subject_id",
                schema: "web_health",
                table: "website",
                column: "owner_subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_updated_by_user_id",
                schema: "web_health",
                table: "website",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_tag_created_by_user_id",
                schema: "web_health",
                table: "website_tag",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_website_tag_tag_id",
                schema: "web_health",
                table: "website_tag",
                column: "tag_id");

            migrationBuilder.Sql("""
                CREATE FUNCTION web_health.enforce_enabled_website_environment()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF NEW.is_enabled
                       AND NOT EXISTS (
                           SELECT 1
                           FROM web_health.environment AS environment
                           WHERE environment.website_id = NEW.id
                             AND environment.is_active
                             AND environment.deleted_at IS NULL)
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'ck_website_enabled_requires_active_environment',
                            MESSAGE = 'An enabled website requires at least one active environment.';
                    END IF;
                    RETURN NULL;
                END;
                $function$;

                CREATE CONSTRAINT TRIGGER ck_website_enabled_requires_active_environment
                AFTER INSERT OR UPDATE ON web_health.website
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION web_health.enforce_enabled_website_environment();

                CREATE FUNCTION web_health.enforce_environment_keeps_enabled_website_valid()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    affected_website_id uuid;
                BEGIN
                    FOR affected_website_id IN
                        SELECT DISTINCT candidate.website_id
                        FROM (VALUES
                            (CASE WHEN TG_OP <> 'INSERT' THEN OLD.website_id END),
                            (CASE WHEN TG_OP <> 'DELETE' THEN NEW.website_id END))
                            AS candidate(website_id)
                        WHERE candidate.website_id IS NOT NULL
                    LOOP
                        IF EXISTS (
                               SELECT 1
                               FROM web_health.website AS website
                               WHERE website.id = affected_website_id
                                 AND website.is_enabled)
                           AND NOT EXISTS (
                               SELECT 1
                               FROM web_health.environment AS environment
                               WHERE environment.website_id = affected_website_id
                                 AND environment.is_active
                                 AND environment.deleted_at IS NULL)
                        THEN
                            RAISE EXCEPTION USING
                                ERRCODE = '23514',
                                CONSTRAINT = 'ck_website_enabled_requires_active_environment',
                                MESSAGE = 'An enabled website requires at least one active environment.';
                        END IF;
                    END LOOP;
                    RETURN NULL;
                END;
                $function$;

                CREATE CONSTRAINT TRIGGER ck_environment_keeps_enabled_website_valid
                AFTER INSERT OR UPDATE OR DELETE ON web_health.environment
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION web_health.enforce_environment_keeps_enabled_website_valid();

                CREATE FUNCTION web_health.validate_production_endpoint(
                    target_endpoint_id uuid,
                    verify_approver_role boolean)
                RETURNS void
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target record;
                BEGIN
                    SELECT endpoint.normalized_url,
                           endpoint.http_exception_reason,
                           endpoint.http_exception_approved_by_user_id,
                           endpoint.http_exception_approved_at,
                           environment.is_production
                    INTO target
                    FROM web_health.endpoint AS endpoint
                    JOIN web_health.environment AS environment
                      ON environment.id = endpoint.environment_id
                    WHERE endpoint.id = target_endpoint_id;

                    IF target.is_production
                       AND target.normalized_url LIKE 'http://%'
                       AND (target.http_exception_reason IS NULL
                            OR length(btrim(target.http_exception_reason)) = 0
                            OR target.http_exception_approved_by_user_id IS NULL
                            OR target.http_exception_approved_at IS NULL
                            OR (verify_approver_role AND NOT EXISTS (
                                SELECT 1
                                FROM web_health.app_user_role AS user_role
                                JOIN web_health.app_role AS role ON role.id = user_role.role_id
                                WHERE user_role.user_id = target.http_exception_approved_by_user_id
                                  AND role.normalized_name = 'ADMINISTRATOR')))
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'ck_production_http_endpoint_admin_exception',
                            MESSAGE = 'Production HTTP endpoints require an Administrator-approved exception reason.';
                    END IF;
                END;
                $function$;

                CREATE FUNCTION web_health.enforce_production_endpoint()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    PERFORM web_health.validate_production_endpoint(
                        NEW.id,
                        CASE WHEN TG_OP = 'INSERT' THEN TRUE ELSE
                            NEW.environment_id IS DISTINCT FROM OLD.environment_id
                            OR (NEW.normalized_url LIKE 'http://%'
                                AND NEW.normalized_url IS DISTINCT FROM OLD.normalized_url)
                            OR NEW.http_exception_reason IS DISTINCT FROM OLD.http_exception_reason
                            OR NEW.http_exception_approved_by_user_id IS DISTINCT FROM OLD.http_exception_approved_by_user_id
                            OR NEW.http_exception_approved_at IS DISTINCT FROM OLD.http_exception_approved_at
                        END);
                    RETURN NULL;
                END;
                $function$;

                CREATE CONSTRAINT TRIGGER ck_production_http_endpoint_admin_exception
                AFTER INSERT OR UPDATE ON web_health.endpoint
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION web_health.enforce_production_endpoint();

                CREATE FUNCTION web_health.enforce_production_environment_endpoints()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    target_endpoint_id uuid;
                    verify_approver_role boolean;
                BEGIN
                    verify_approver_role = TG_OP = 'INSERT' OR NOT OLD.is_production;
                    IF NEW.is_production THEN
                        FOR target_endpoint_id IN
                            SELECT endpoint.id
                            FROM web_health.endpoint AS endpoint
                            WHERE endpoint.environment_id = NEW.id
                              AND endpoint.deleted_at IS NULL
                        LOOP
                            PERFORM web_health.validate_production_endpoint(
                                target_endpoint_id,
                                verify_approver_role);
                        END LOOP;
                    END IF;
                    RETURN NULL;
                END;
                $function$;

                CREATE CONSTRAINT TRIGGER ck_production_environment_endpoints
                AFTER INSERT OR UPDATE ON web_health.environment
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION web_health.enforce_production_environment_endpoints();

                CREATE FUNCTION web_health.enforce_monitor_policy_type()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM web_health.policy_profile AS profile
                        WHERE profile.id = NEW.policy_profile_id
                          AND profile.monitor_type = NEW.monitor_type
                          AND profile.deleted_at IS NULL)
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'ck_endpoint_monitor_policy_type',
                            MESSAGE = 'Endpoint monitor type must match its active policy profile.';
                    END IF;
                    RETURN NULL;
                END;
                $function$;

                CREATE CONSTRAINT TRIGGER ck_endpoint_monitor_policy_type
                AFTER INSERT OR UPDATE ON web_health.endpoint_monitor
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION web_health.enforce_monitor_policy_type();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS ck_endpoint_monitor_policy_type ON web_health.endpoint_monitor;
                DROP TRIGGER IF EXISTS ck_production_environment_endpoints ON web_health.environment;
                DROP TRIGGER IF EXISTS ck_production_http_endpoint_admin_exception ON web_health.endpoint;
                DROP TRIGGER IF EXISTS ck_environment_keeps_enabled_website_valid ON web_health.environment;
                DROP TRIGGER IF EXISTS ck_website_enabled_requires_active_environment ON web_health.website;
                DROP FUNCTION IF EXISTS web_health.enforce_monitor_policy_type();
                DROP FUNCTION IF EXISTS web_health.enforce_production_environment_endpoints();
                DROP FUNCTION IF EXISTS web_health.enforce_production_endpoint();
                DROP FUNCTION IF EXISTS web_health.validate_production_endpoint(uuid, boolean);
                DROP FUNCTION IF EXISTS web_health.enforce_environment_keeps_enabled_website_valid();
                DROP FUNCTION IF EXISTS web_health.enforce_enabled_website_environment();
                """);

            migrationBuilder.DropTable(
                name: "access_grant",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "endpoint_monitor",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "target_authorization",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "website_tag",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "policy_profile",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "endpoint",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "tag",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "environment",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "website",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "client",
                schema: "web_health");
        }
    }
}
