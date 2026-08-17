using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebHealth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LogicalCheckExecutionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_execution_attempt_outcome",
                schema: "web_health",
                table: "execution_attempt");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_result_outcome_category",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_attempt_outcome",
                schema: "web_health",
                table: "execution_attempt",
                sql: "infrastructure_outcome IN ('Running', 'Succeeded', 'RetryableFailure', 'TerminalFailure', 'Cancelled', 'Superseded')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result",
                sql: "failure_category IS NULL OR failure_category IN ('Dns','Connection','Tls','Timeout','Cancellation','ClientError','ServerError','RedirectLoop','ExcessiveRedirects','ContentMismatch','ResponseTooLarge','HttpsRequired','InvalidConfiguration','DestinationPolicy','InvalidRedirect','ExecutionExhausted','TargetIneligible','Protocol')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_result_outcome_category",
                schema: "web_health",
                table: "check_result",
                sql: "(outcome = 'Healthy' AND failure_category IS NULL) OR (outcome = 'Cancelled' AND failure_category IN ('Cancellation','TargetIneligible')) OR (outcome IN ('Warning','Critical') AND failure_category IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_execution_attempt_outcome",
                schema: "web_health",
                table: "execution_attempt");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.DropCheckConstraint(
                name: "ck_check_result_outcome_category",
                schema: "web_health",
                table: "check_result");

            migrationBuilder.Sql(
                "UPDATE web_health.execution_attempt "
                + "SET infrastructure_outcome = 'RetryableFailure', "
                + "failure_category = COALESCE(failure_category, 'LeaseSuperseded') "
                + "WHERE infrastructure_outcome = 'Superseded'");
            migrationBuilder.Sql(
                "UPDATE web_health.check_result "
                + "SET failure_category = 'Cancellation' "
                + "WHERE failure_category = 'TargetIneligible'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_execution_attempt_outcome",
                schema: "web_health",
                table: "execution_attempt",
                sql: "infrastructure_outcome IN ('Running', 'Succeeded', 'RetryableFailure', 'TerminalFailure', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_result_failure_category",
                schema: "web_health",
                table: "check_result",
                sql: "failure_category IS NULL OR failure_category IN ('Dns','Connection','Tls','Timeout','Cancellation','ClientError','ServerError','RedirectLoop','ExcessiveRedirects','ContentMismatch','ResponseTooLarge','HttpsRequired','InvalidConfiguration','DestinationPolicy','InvalidRedirect','ExecutionExhausted','Protocol')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_check_result_outcome_category",
                schema: "web_health",
                table: "check_result",
                sql: "(outcome = 'Healthy' AND failure_category IS NULL) OR (outcome = 'Cancelled' AND failure_category = 'Cancellation') OR (outcome IN ('Warning','Critical') AND failure_category IS NOT NULL)");
        }
    }
}
