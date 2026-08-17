using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DurableNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_event",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    event_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    occurrence_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    template_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_suppressed = table.Column<bool>(type: "boolean", nullable: false),
                    suppression_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_event", x => x.id);
                    table.CheckConstraint("ck_notification_event_incident_event_required", "(source_kind = 'IncidentEvent' AND incident_event_id IS NOT NULL) OR (source_kind IN ('Reminder', 'Escalation') AND incident_event_id IS NULL)");
                    table.CheckConstraint("ck_notification_event_source_kind", "source_kind IN ('IncidentEvent', 'Reminder', 'Escalation')");
                    table.CheckConstraint("ck_notification_event_suppression_fields", "(is_suppressed AND suppression_reason IS NOT NULL AND length(suppression_reason) > 0) OR (NOT is_suppressed AND suppression_reason IS NULL)");
                    table.CheckConstraint("ck_notification_event_type", "event_type IN ('Opened', 'Recovered', 'Reminder', 'Escalated')");
                    table.ForeignKey(
                        name: "fk_notification_event_incident_event_incident_event_id",
                        column: x => x.incident_event_id,
                        principalSchema: "web_health",
                        principalTable: "incident_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notification_event_incident_incident_id",
                        column: x => x.incident_id,
                        principalSchema: "web_health",
                        principalTable: "incident",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_delivery",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    normalized_recipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    recipient_normalization_version = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_delivery", x => x.id);
                    table.CheckConstraint("ck_notification_delivery_attempt_count", "attempt_count >= 0");
                    table.CheckConstraint("ck_notification_delivery_channel", "channel IN ('Email')");
                    table.CheckConstraint("ck_notification_delivery_lease_fields", "(lease_owner IS NULL AND lease_expires_at IS NULL) OR (lease_owner IS NOT NULL AND lease_expires_at IS NOT NULL)");
                    table.CheckConstraint("ck_notification_delivery_sent_fields", "(state = 'Sent' AND sent_at IS NOT NULL) OR (state <> 'Sent' AND sent_at IS NULL)");
                    table.CheckConstraint("ck_notification_delivery_state", "state IN ('Pending', 'Processing', 'Sent', 'RetryScheduled', 'FailedPermanently', 'Suppressed')");
                    table.ForeignKey(
                        name: "fk_notification_delivery_notification_event_id",
                        column: x => x.notification_event_id,
                        principalSchema: "web_health",
                        principalTable: "notification_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_attempt",
                schema: "web_health",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    transport_outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    safe_response = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_attempt", x => x.id);
                    table.CheckConstraint("ck_notification_attempt_number", "attempt_number > 0");
                    table.CheckConstraint("ck_notification_attempt_outcome", "transport_outcome IN ('Sent', 'TransientFailure', 'PermanentFailure')");
                    table.ForeignKey(
                        name: "fk_notification_attempt_notification_delivery_id",
                        column: x => x.notification_delivery_id,
                        principalSchema: "web_health",
                        principalTable: "notification_delivery",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_notification_attempt_number",
                schema: "web_health",
                table: "notification_attempt",
                columns: new[] { "notification_delivery_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_state_next_attempt_at",
                schema: "web_health",
                table: "notification_delivery",
                columns: new[] { "state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ux_notification_delivery_recipient",
                schema: "web_health",
                table: "notification_delivery",
                columns: new[] { "notification_event_id", "channel", "normalized_recipient" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_event_incident_event_id",
                schema: "web_health",
                table: "notification_event",
                column: "incident_event_id");

            migrationBuilder.CreateIndex(
                name: "ux_notification_event_occurrence",
                schema: "web_health",
                table: "notification_event",
                columns: new[] { "incident_id", "source_kind", "event_type", "occurrence_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_attempt",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "notification_delivery",
                schema: "web_health");

            migrationBuilder.DropTable(
                name: "notification_event",
                schema: "web_health");
        }
    }
}
