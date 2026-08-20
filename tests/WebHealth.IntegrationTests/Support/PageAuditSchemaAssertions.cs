using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.PageAudits;
using WebHealth.Infrastructure.Persistence;
using Xunit;

namespace WebHealth.IntegrationTests.Support;

/// <summary>
/// The page-audit tables enforce their own contract. Every rule below is asserted against
/// PostgreSQL rather than against the services, because a service is one caller and a constraint
/// covers every caller — including a future one written by somebody who has not read this file.
/// </summary>
internal static class PageAuditSchemaAssertions
{
    public static async Task VerifyAsync(
        string connectionString,
        ApplicationDbContext database,
        Guid endpointId)
    {
        await VerifyNoRawPayloadOrSecretColumnAsync(connectionString);

        var now = DateTimeOffset.UtcNow;
        var targetId = await SeedTargetAsync(database, endpointId, now);

        await VerifyTargetProfileIsUniqueAsync(connectionString, endpointId);
        await VerifyIntervalBoundsAsync(connectionString, targetId);
        await VerifySchedulingRequiresEnabledAsync(connectionString, targetId);

        await VerifyOnlyOneActiveRunPerTargetAsync(connectionString, targetId, endpointId);
        await VerifyTerminalRunContractAsync(connectionString, targetId, endpointId);
        await VerifyCompletedRunNeedsAScoreAsync(connectionString, targetId, endpointId);
        await VerifyFailedRunNeedsACategoryAsync(connectionString, targetId, endpointId);
        await VerifyLeasePairMovesTogetherAsync(connectionString, targetId, endpointId);
        await VerifyScoreRangeAsync(connectionString, targetId, endpointId);

        await VerifyItemUniquePerRunAsync(database, targetId, endpointId, now);
        await VerifyScoredItemNeedsAScoreAsync(connectionString, targetId, endpointId, now);
    }

    /// <summary>
    /// The storage decision, asserted by name. No column can hold the provider's full response,
    /// a screenshot, a trace, free-form audit details, or the API key — so none of them can be
    /// retained by accident later, whatever a future writer intends.
    /// </summary>
    private static async Task VerifyNoRawPayloadOrSecretColumnAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name || '.' || column_name
            FROM information_schema.columns
            WHERE table_schema = 'web_health'
              AND table_name LIKE 'page_audit%'
              AND (column_name LIKE '%raw_json%' OR column_name LIKE '%report_json%'
                   OR column_name LIKE '%payload%' OR column_name LIKE '%screenshot%'
                   OR column_name LIKE '%trace%' OR column_name = 'details'
                   OR column_name LIKE '%api_key%' OR column_name LIKE '%secret%'
                   OR column_name LIKE '%request_uri%');
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var offending = new List<string>();
        while (await reader.ReadAsync()) offending.Add(reader.GetString(0));
        offending.Should().BeEmpty(
            "the page-audit schema stores normalized values only; a column able to hold the raw "
            + "response or the API key would make retaining one an accident away");
    }

    private static async Task<Guid> SeedTargetAsync(
        ApplicationDbContext database,
        Guid endpointId,
        DateTimeOffset now)
    {
        var targetId = Guid.NewGuid();
        database.PageAuditTargets.Add(new PageAuditTarget
        {
            Id = targetId,
            EndpointId = endpointId,
            Provider = PageAuditProviders.PageSpeedInsights,
            Category = PageAuditCategories.Seo,
            Strategy = PageAuditStrategies.Mobile,
            IsEnabled = true,
            SchedulingEnabled = true,
            IntervalSeconds = 86400,
            ScheduleAnchor = now,
            NextDueAt = now.AddDays(1),
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        return targetId;
    }

    private static Task VerifyTargetProfileIsUniqueAsync(string connectionString, Guid endpointId) =>
        ExpectRejectionAsync(
            connectionString,
            $"""
            INSERT INTO web_health.page_audit_target
                (id, endpoint_id, provider, category, strategy, is_enabled, scheduling_enabled,
                 interval_seconds, schedule_anchor, next_due_at, created_at, updated_at, version)
            VALUES ('{Guid.NewGuid()}', '{endpointId}', 'PageSpeedInsights', 'Seo', 'Mobile',
                    TRUE, TRUE, 86400, now(), now(), now(), now(), 1);
            """,
            PostgresErrorCodes.UniqueViolation,
            "ux_page_audit_target_profile");

    private static Task VerifyIntervalBoundsAsync(string connectionString, Guid targetId) =>
        ExpectRejectionAsync(
            connectionString,
            $"UPDATE web_health.page_audit_target SET interval_seconds = 60 WHERE id = '{targetId}';",
            PostgresErrorCodes.CheckViolation,
            "ck_page_audit_target_interval");

    private static Task VerifySchedulingRequiresEnabledAsync(string connectionString, Guid targetId) =>
        ExpectRejectionAsync(
            connectionString,
            "UPDATE web_health.page_audit_target SET is_enabled = FALSE, scheduling_enabled = TRUE "
            + $"WHERE id = '{targetId}';",
            PostgresErrorCodes.CheckViolation,
            "ck_page_audit_target_scheduling_requires_enabled");

    /// <summary>
    /// The rule that stops a dispatcher and a person pressing Run now from spending two API calls
    /// on the same audit, and that makes a spurious re-enqueue harmless instead of duplicating work.
    /// </summary>
    private static async Task VerifyOnlyOneActiveRunPerTargetAsync(
        string connectionString,
        Guid targetId,
        Guid endpointId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await ExecuteAsync(connection, transaction, RunInsert(Guid.NewGuid(), targetId, endpointId, "Queued"));

        // A savepoint, because the rejection below aborts the transaction and every later
        // statement in it would then fail with 25P02 rather than with the constraint it is
        // testing. Rolling back to the savepoint leaves the Queued run and a usable transaction.
        await transaction.SaveAsync("duplicate_active_run");
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connection, transaction, RunInsert(Guid.NewGuid(), targetId, endpointId, "Running")));
        duplicate.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        duplicate.ConstraintName.Should().Be("ux_page_audit_run_active");
        await transaction.RollbackAsync("duplicate_active_run");

        // The index is partial, so a target may hold any number of finished runs. Without this the
        // uniqueness rule would cap an endpoint's history at one row.
        await ExecuteAsync(connection, transaction, RunInsert(
            Guid.NewGuid(), targetId, endpointId, "Completed",
            extraColumns: ", finished_at, raw_score, lighthouse_version",
            extraValues: ", now(), 0.82, '11.4.0'"));
        await ExecuteAsync(connection, transaction, RunInsert(
            Guid.NewGuid(), targetId, endpointId, "Completed",
            extraColumns: ", finished_at, raw_score, lighthouse_version",
            extraValues: ", now(), 0.91, '11.4.0'"));

        await transaction.RollbackAsync();
    }

    private static Task VerifyTerminalRunContractAsync(
        string connectionString, Guid targetId, Guid endpointId) =>
        ExpectRejectionAsync(
            connectionString,
            RunInsert(Guid.NewGuid(), targetId, endpointId, "Failed",
                extraColumns: ", failure_category",
                extraValues: ", 'ProviderTimeout'"),
            PostgresErrorCodes.CheckViolation,
            "ck_page_audit_run_finished_when_terminal");

    private static Task VerifyCompletedRunNeedsAScoreAsync(
        string connectionString, Guid targetId, Guid endpointId) =>
        ExpectRejectionAsync(
            connectionString,
            RunInsert(Guid.NewGuid(), targetId, endpointId, "Completed",
                extraColumns: ", finished_at, lighthouse_version",
                extraValues: ", now(), '11.4.0'"),
            PostgresErrorCodes.CheckViolation,
            "ck_page_audit_run_completed_contract");

    private static Task VerifyFailedRunNeedsACategoryAsync(
        string connectionString, Guid targetId, Guid endpointId) =>
        ExpectRejectionAsync(
            connectionString,
            RunInsert(Guid.NewGuid(), targetId, endpointId, "Failed",
                extraColumns: ", finished_at",
                extraValues: ", now()"),
            PostgresErrorCodes.CheckViolation,
            "ck_page_audit_run_failure_contract");

    private static Task VerifyLeasePairMovesTogetherAsync(
        string connectionString, Guid targetId, Guid endpointId) =>
        ExpectRejectionAsync(
            connectionString,
            RunInsert(Guid.NewGuid(), targetId, endpointId, "Running",
                extraColumns: ", lease_token",
                extraValues: $", '{Guid.NewGuid()}'"),
            PostgresErrorCodes.CheckViolation,
            "ck_page_audit_run_lease_pair");

    private static Task VerifyScoreRangeAsync(
        string connectionString, Guid targetId, Guid endpointId) =>
        ExpectRejectionAsync(
            connectionString,
            RunInsert(Guid.NewGuid(), targetId, endpointId, "Completed",
                extraColumns: ", finished_at, raw_score, lighthouse_version",
                extraValues: ", now(), 1.5, '11.4.0'"),
            PostgresErrorCodes.CheckViolation,
            "ck_page_audit_run_raw_score");

    /// <summary>
    /// One row per audit per run. A retried finalization has to update a run's items, never append
    /// a second copy of every audit it already recorded.
    /// </summary>
    private static async Task VerifyItemUniquePerRunAsync(
        ApplicationDbContext database,
        Guid targetId,
        Guid endpointId,
        DateTimeOffset now)
    {
        var runId = Guid.NewGuid();
        database.PageAuditRuns.Add(new PageAuditRun
        {
            Id = runId,
            PageAuditTargetId = targetId,
            EndpointId = endpointId,
            Source = PageAuditSources.Scheduled,
            Status = PageAuditRunStatuses.Completed,
            RequestedUrl = "https://page-audit-schema.test/",
            RawScore = 0.82m,
            Provider = PageAuditProviders.PageSpeedInsights,
            Category = PageAuditCategories.Seo,
            Strategy = PageAuditStrategies.Mobile,
            Locale = "en-US",
            LighthouseVersion = "11.4.0",
            AttemptCount = 1,
            QueuedAt = now,
            FinishedAt = now,
            UpdatedAt = now
        });
        database.PageAuditItems.Add(new PageAuditItem
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            AuditId = "document-title",
            Status = PageAuditItemStatuses.Passed,
            Score = 1m,
            ScoreDisplayMode = PageAuditScoreDisplayModes.Binary,
            Weight = 10
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        // Added through the DbSet, not through run.Items: keys here are client-generated, so EF
        // would attach an entity added to a loaded collection as an existing row and emit an
        // UPDATE against an id that was never inserted.
        database.PageAuditItems.Add(new PageAuditItem
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            AuditId = "document-title",
            Status = PageAuditItemStatuses.Failed,
            Score = 0m,
            ScoreDisplayMode = PageAuditScoreDisplayModes.Binary,
            Weight = 10
        });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        duplicate.InnerException.Should().BeOfType<PostgresException>()
            .Which.ConstraintName.Should().Be("ux_page_audit_item_run_audit");
        database.ChangeTracker.Clear();
    }

    private static async Task VerifyScoredItemNeedsAScoreAsync(
        string connectionString,
        Guid targetId,
        Guid endpointId,
        DateTimeOffset now)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var runId = Guid.NewGuid();
        await ExecuteAsync(connection, transaction, RunInsert(
            runId, targetId, endpointId, "Completed",
            extraColumns: ", finished_at, raw_score, lighthouse_version",
            extraValues: ", now(), 0.82, '11.4.0'"));

        await transaction.SaveAsync("scored_item_without_a_score");
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            connection, transaction,
            $"""
            INSERT INTO web_health.page_audit_item
                (id, run_id, audit_id, status, score_display_mode, weight)
            VALUES ('{Guid.NewGuid()}', '{runId}', 'meta-description', 'Failed', 'binary', 10);
            """));
        rejected.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        rejected.ConstraintName.Should().Be("ck_page_audit_item_scored_statuses_have_a_score");
        await transaction.RollbackAsync("scored_item_without_a_score");

        // A manual audit legitimately carries no score, which is exactly why the rule above is
        // scoped to the scored statuses rather than applied to every row.
        await ExecuteAsync(connection, transaction,
            $"""
            INSERT INTO web_health.page_audit_item
                (id, run_id, audit_id, status, score_display_mode, weight)
            VALUES ('{Guid.NewGuid()}', '{runId}', 'structured-data', 'Manual', 'manual', 0);
            """);

        await transaction.RollbackAsync();
    }

    private static string RunInsert(
        Guid runId,
        Guid targetId,
        Guid endpointId,
        string status,
        string extraColumns = "",
        string extraValues = "") =>
        $"""
        INSERT INTO web_health.page_audit_run
            (id, page_audit_target_id, endpoint_id, source, status, requested_url, provider,
             category, strategy, locale, attempt_count, queued_at, updated_at{extraColumns})
        VALUES ('{runId}', '{targetId}', '{endpointId}', 'Scheduled', '{status}',
                'https://page-audit-schema.test/', 'PageSpeedInsights', 'Seo', 'Mobile', 'en-US',
                0, now(), now(){extraValues});
        """;

    private static async Task ExpectRejectionAsync(
        string connectionString,
        string sql,
        string expectedSqlState,
        string expectedConstraint)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var rejection = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(connection, transaction, sql));
        rejection.SqlState.Should().Be(expectedSqlState);
        rejection.ConstraintName.Should().Be(expectedConstraint);

        await transaction.RollbackAsync();
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
