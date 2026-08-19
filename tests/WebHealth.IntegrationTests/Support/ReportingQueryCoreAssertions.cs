using System.Globalization;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Application.Reporting;
using WebHealth.Domain.Health;
using WebHealth.Domain.Incidents;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.IntegrationTests.Support;

/// <summary>
/// Increment 5.5's evidence, against a real PostgreSQL cluster because the parts that can go
/// wrong are the parts PostgreSQL evaluates: <c>percentile_cont</c>, the <c>[start, end)</c>
/// window predicate, and the eligibility filters behind uptime.
/// </summary>
internal static class ReportingQueryCoreAssertions
{
    private static readonly DateTimeOffset WindowStart = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 7, 8, 0, 0, 0, TimeSpan.Zero);

    public static async Task VerifyAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection().AddLogging()
            .AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reader = scope.ServiceProvider.GetRequiredService<IReportingReader>();
        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var access = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);

        var fixture = await SeedAsync(scope, database, access);

        await VerifyUptimeCountsOnlyEligibleSamplesAsync(reader, access, fixture);
        await VerifyWindowIsHalfOpenAsync(reader, access, fixture);
        await VerifyPercentilesUseSuccessfulSamplesOnlyAsync(reader, access, fixture);
        await VerifyTrendBucketsByUtcDayAsync(reader, access, fixture);
        await VerifyScreenAndCsvSelectTheSameRecordsAsync(reader, access, fixture);
        await VerifyIncidentListAndCountDescribeOneSelectionAsync(reader, access, fixture);
        await VerifyAPageBeyondTheEndReportsThePageItServedAsync(reader, access, fixture);
        await VerifyVisibilityIsAppliedToBothSurfacesAsync(database, reader, fixture);
    }

    /// <summary>
    /// BR-U01–BR-U03. The denominator is what the finalizer marked eligible: manual runs,
    /// maintenance-suppressed runs, cancelled runs and certificate results are excluded, and the
    /// excluded count is reported so the exclusion is visible rather than silent.
    /// </summary>
    private static async Task VerifyUptimeCountsOnlyEligibleSamplesAsync(
        IReportingReader reader,
        RegistryAccessContext access,
        ReportingFixture fixture)
    {
        var query = Query(fixture, monitorType: null);
        var rows = await ReadEveryScreenPageAsync(reader, query, access);
        var row = rows.Single(candidate => candidate.EndpointMonitorId == fixture.HttpMonitorId);

        // Seeded: 6 eligible (4 healthy, 1 warning, 1 critical) and 2 ineligible.
        row.Uptime.EligibleSamples.Should().Be(6);
        row.Uptime.HealthySamples.Should().Be(4);
        row.Uptime.WarningSamples.Should().Be(1);
        row.Uptime.DownSamples.Should().Be(1);
        row.Uptime.ExcludedSamples.Should().Be(2);

        // BR-U01 as written: healthy over eligible. The warning sample raises neither this
        // figure nor the downtime, which is exactly why it is reported on its own.
        row.Uptime.Percentage.Should().BeApproximately(66.6667, 0.001);
        row.Uptime.ReachablePercentage.Should().BeApproximately(83.3333, 0.001);
        (row.Uptime.HealthySamples + row.Uptime.WarningSamples + row.Uptime.DownSamples)
            .Should().Be(row.Uptime.EligibleSamples, "every eligible sample lands in exactly one category");

        // A certificate monitor never contributes an availability sample, so its row is present
        // with nothing to report rather than absent or zero-per-cent.
        var certificateRow = rows.Single(candidate =>
            candidate.EndpointMonitorId == fixture.SslMonitorId);
        certificateRow.Uptime.EligibleSamples.Should().Be(0);
        certificateRow.Uptime.Percentage.Should().BeNull();
        certificateRow.Uptime.ExcludedSamples.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// BR-U04: a sample measured exactly at the window's end belongs to the next period. The
    /// seed puts one sample on each boundary instant so both sides of the rule are proven.
    /// </summary>
    private static async Task VerifyWindowIsHalfOpenAsync(
        IReportingReader reader,
        RegistryAccessContext access,
        ReportingFixture fixture)
    {
        var rows = await ReadEveryScreenPageAsync(reader, Query(fixture, monitorType: null), access);
        var row = rows.Single(candidate => candidate.EndpointMonitorId == fixture.HttpMonitorId);

        // The boundary sample at WindowStart is counted; the one at WindowEnd is not. Shifting
        // the window one week later must pick up exactly that excluded sample.
        var nextPeriod = await ReadEveryScreenPageAsync(
            reader,
            Query(fixture, monitorType: null, start: WindowEnd, end: WindowEnd.AddDays(7)),
            access);
        var nextRow = nextPeriod.Single(candidate => candidate.EndpointMonitorId == fixture.HttpMonitorId);

        row.Uptime.EligibleSamples.Should().Be(6);
        nextRow.Uptime.EligibleSamples.Should().Be(1);
        nextRow.LastMeasuredAt.Should().Be(WindowEnd);
    }

    /// <summary>
    /// BR-U05. The seeded durations are 100, 200, 300, 400 and 1,800 ms across samples that
    /// answered — the 1,800 ms one being the warning sample, whose duration is a real
    /// measurement — plus a 15,000 ms timeout that must stay out of the ordering entirely.
    /// </summary>
    private static async Task VerifyPercentilesUseSuccessfulSamplesOnlyAsync(
        IReportingReader reader,
        RegistryAccessContext access,
        ReportingFixture fixture)
    {
        var rows = await ReadEveryScreenPageAsync(reader, Query(fixture, monitorType: null), access);
        var row = rows.Single(candidate => candidate.EndpointMonitorId == fixture.HttpMonitorId);

        row.ResponseTimes.MeasuredSamples.Should().Be(5);

        // percentile_cont over [100, 200, 300, 400, 1800]: the median is the middle value, and
        // P95 interpolates between the top two rather than snapping to 1,800 the way
        // percentile_disc would.
        row.ResponseTimes.P50Ms.Should().Be(300);
        row.ResponseTimes.P95Ms.Should().BeApproximately(1_520, 0.001);

        // The 15,000 ms timeout is the proof: had it entered the ordering, P95 would sit near it.
        row.ResponseTimes.P95Ms.Should().BeLessThan(15_000);
    }

    private static async Task VerifyTrendBucketsByUtcDayAsync(
        IReportingReader reader,
        RegistryAccessContext access,
        ReportingFixture fixture)
    {
        var dataset = await reader.QueryAsync(Query(fixture, monitorType: null), access);

        dataset.Trend.Should().NotBeEmpty();
        dataset.Trend.Should().BeInAscendingOrder(point => point.Day);
        dataset.Trend.Select(point => point.Day).Should().OnlyHaveUniqueItems();
        dataset.Trend.Sum(point => point.EligibleSamples)
            .Should().Be(dataset.Summary.Uptime.EligibleSamples);
        dataset.Trend.Sum(point => point.HealthySamples)
            .Should().Be(dataset.Summary.Uptime.HealthySamples);
        dataset.Trend.Should().OnlyContain(point =>
            point.Day >= DateOnly.FromDateTime(WindowStart.UtcDateTime)
            && point.Day < DateOnly.FromDateTime(WindowEnd.UtcDateTime));

        // The trend and the card above it must use the same sample categories: the day holding
        // the 15,000 ms timeout must not report it as a percentile either.
        var timeoutDay = dataset.Trend.Single(point =>
            point.Day == DateOnly.FromDateTime(WindowStart.AddDays(5).UtcDateTime));
        timeoutDay.HealthySamples.Should().Be(0);
        timeoutDay.P95Ms.Should().BeNull("the only sample that day was a failed exchange");
    }

    /// <summary>
    /// The list and the count are two renderings of one selection. Reading the list through a
    /// separate filter is how a dashboard ends up showing a count for one dataset and rows from
    /// another.
    /// </summary>
    private static async Task VerifyIncidentListAndCountDescribeOneSelectionAsync(
        IReportingReader reader,
        RegistryAccessContext access,
        ReportingFixture fixture)
    {
        // A filter that selects nothing must yield no incidents, whatever is open elsewhere.
        var elsewhere = Query(fixture, monitorType: null, clientId: Guid.NewGuid());
        (await reader.QueryActiveIncidentsAsync(elsewhere, access, 10)).Should().BeEmpty();
        (await reader.QueryAsync(elsewhere, access)).Summary.ActiveIncidentCount.Should().Be(0);

        var query = Query(fixture, monitorType: null);
        var dataset = await reader.QueryAsync(query, access);
        var incidents = await reader.QueryActiveIncidentsAsync(query, access, 1_000);

        incidents.Should().HaveCount(dataset.Summary.ActiveIncidentCount);
        incidents.Select(incident => incident.Status)
            .Should().OnlyContain(status => IncidentStatuses.Active.Contains(status));
    }

    /// <summary>
    /// A page beyond the end serves the last page and says so. Returning the requested page
    /// would render "page 999,999 of 2" with pagination links that go nowhere.
    /// </summary>
    private static async Task VerifyAPageBeyondTheEndReportsThePageItServedAsync(
        IReportingReader reader,
        RegistryAccessContext access,
        ReportingFixture fixture)
    {
        var dataset = await reader.QueryAsync(
            Query(fixture, monitorType: null).WithPaging(999_999), access);

        dataset.Query.Page.Should().Be(dataset.TotalPages);
        dataset.Rows.Should().NotBeEmpty();
    }

    /// <summary>
    /// AC-11, as a direct record-identity comparison. For every filter combination the screen's
    /// rows are parsed back out of the CSV and compared field by field. There is no assertion
    /// here about "similar" datasets: the identifiers, the counts and the percentiles all have
    /// to match, because both came from one query.
    /// </summary>
    private static async Task VerifyScreenAndCsvSelectTheSameRecordsAsync(
        IReportingReader reader,
        RegistryAccessContext access,
        ReportingFixture fixture)
    {
        var covered = 0;
        foreach (var query in EveryFilterCombination(fixture))
        {
            var screenRows = await ReadEveryScreenPageAsync(reader, query, access);

            // The export re-slices the caller's filter itself, so this deliberately hands it a
            // query still sitting on the screen's page: an export that honoured that page would
            // produce a different file from the same filter.
            var export = await reader.ExportAsync(query, access);

            export.Query.Page.Should().Be(1);
            export.TotalCount.Should().Be(screenRows.Count);

            // ReportRow is a record, so this is structural equality across every field —
            // identifiers, counts, uptime and both percentiles — in order.
            export.Rows.Should().Equal(
                screenRows,
                "the screen and the export are one query at two page sizes");

            // And the bytes a recipient actually opens carry exactly those records, read back
            // the way a spreadsheet would read them rather than trusted as written.
            var csvRows = ParseCsv(ReportCsv.Write(export));
            csvRows.Should().HaveCount(screenRows.Count);
            csvRows.Select(row => row[0])
                .Should().Equal(screenRows.Select(row => row.EndpointMonitorId.ToString()));
            csvRows.Select(row => row[Array.IndexOf(ReportCsv.Headers.ToArray(), "UptimePercent")])
                .Should().Equal(screenRows.Select(row => Rendered(row.Uptime.Percentage)));

            covered++;
        }

        // A guard on the guard: an empty combination set would make every assertion above
        // vacuous while still passing.
        covered.Should().BeGreaterThan(50);
    }

    private static string Rendered(double? value) =>
        value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// A caller who cannot see an endpoint must not be able to reach it through either surface.
    /// Testing the export separately matters: an export that skipped the visibility scope would
    /// be a data-disclosure route that no screen assertion would ever catch.
    /// </summary>
    private static async Task VerifyVisibilityIsAppliedToBothSurfacesAsync(
        ApplicationDbContext database,
        IReportingReader reader,
        ReportingFixture fixture)
    {
        var viewer = await database.Users.AsNoTracking()
            .Where(user => user.Email != "bootstrap@example.test")
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync()
            ?? Guid.NewGuid();
        var unprivileged = new RegistryAccessContext(viewer, [ApplicationRoles.Viewer]);
        var query = Query(fixture, monitorType: null);

        (await reader.QueryAsync(query, unprivileged)).TotalCount.Should().Be(
            0, "a viewer without a grant can see no endpoint");
        (await reader.QueryAsync(query, unprivileged)).Rows.Should().BeEmpty(
            "a viewer without a grant can see no endpoint");
        (await reader.ExportAsync(query, unprivileged))
            .Rows.Should().BeEmpty("the export applies the same visibility scope as the screen");
        (await reader.QueryActiveIncidentsAsync(query, unprivileged, 10))
            .Should().BeEmpty("the incident list applies the same visibility scope");
        (await reader.QueryCertificateExpiryAsync(query, unprivileged))
            .NeedingAttention.Should().BeEmpty("the certificate card applies the same visibility scope");

        // A scoped reader's selection carries extra grant subqueries that global access never
        // produces, and each filter changes the shape again. Every surface is exercised under
        // every status and monitor-type combination so that a selection which only composes for
        // an administrator cannot pass as working.
        foreach (var scoped in EveryScopedFilter(fixture))
        {
            (await reader.QueryAsync(scoped, unprivileged)).Rows.Should().BeEmpty();
            (await reader.ExportAsync(scoped, unprivileged)).Rows.Should().BeEmpty();
            (await reader.QueryCertificateExpiryAsync(scoped, unprivileged))
                .NeedingAttention.Should().BeEmpty();
            (await reader.QueryDiagnosticsAsync(scoped, unprivileged))
                .ScheduledMonitorCount.Should().Be(0);
            (await reader.QueryActiveIncidentsAsync(scoped, unprivileged, 10)).Should().BeEmpty();
        }
    }

    private static IEnumerable<ReportQuery> EveryScopedFilter(ReportingFixture fixture)
    {
        string?[] statuses =
        [
            null,
            EndpointHealthStatuses.Healthy,
            EndpointHealthStatuses.Unknown,
            EndpointHealthStatuses.Critical
        ];
        string?[] monitorTypes = [null, .. ReportMonitorTypes.All];

        return from status in statuses
               from monitorType in monitorTypes
               from client in new Guid?[] { null, fixture.ClientId }
               select Query(
                   fixture, monitorType, client, null, null, null, status,
                   scopeToFixtureClient: false);
    }

    private static IEnumerable<ReportQuery> EveryFilterCombination(ReportingFixture fixture)
    {
        Guid?[] clients = [null, fixture.ClientId, Guid.NewGuid()];
        Guid?[] websites = [null, fixture.WebsiteId];
        Guid?[] environments = [null, fixture.EnvironmentId];
        Guid?[] owners = [null, fixture.OwnerSubjectId];
        string?[] statuses = [null, EndpointHealthStatuses.Healthy, EndpointHealthStatuses.Critical];
        string?[] monitorTypes = [null, .. ReportMonitorTypes.All];

        return from client in clients
               from website in websites
               from environment in environments
               from owner in owners
               from status in statuses
               from monitorType in monitorTypes
               select Query(
                   fixture, monitorType, client, website, environment, owner, status,
                   scopeToFixtureClient: false);
    }

    private static async Task<IReadOnlyList<ReportRow>> ReadEveryScreenPageAsync(
        IReportingReader reader,
        ReportQuery query,
        RegistryAccessContext access)
    {
        var rows = new List<ReportRow>();
        var first = await reader.QueryAsync(query.WithPaging(1, ReportQueryNormalizer.ScreenPageSize), access);
        rows.AddRange(first.Rows);
        for (var page = 2; page <= first.TotalPages; page++)
        {
            var next = await reader.QueryAsync(
                query.WithPaging(page, ReportQueryNormalizer.ScreenPageSize), access);
            rows.AddRange(next.Rows);
        }

        return rows;
    }

    private static ReportQuery Query(
        ReportingFixture fixture,
        string? monitorType,
        Guid? clientId = null,
        Guid? websiteId = null,
        Guid? environmentId = null,
        Guid? ownerSubjectId = null,
        string? healthStatus = null,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        bool scopeToFixtureClient = true)
    {
        var normalized = ReportQueryNormalizer.Normalize(
            new ReportQueryInput(
                clientId ?? (scopeToFixtureClient ? fixture.ClientId : null),
                websiteId,
                environmentId,
                ownerSubjectId,
                healthStatus,
                monitorType,
                start ?? WindowStart,
                end ?? WindowEnd),
            ReportMonitorTypes.All,
            WindowEnd);
        normalized.Succeeded.Should().BeTrue();
        return normalized.Query!;
    }

    /// <summary>
    /// A minimal RFC 4180 reader. It exists so the comparison is against the bytes actually
    /// written rather than against the object they were written from — the export is only
    /// verified if something reads it back the way a recipient would.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<string>> ParseCsv(byte[] bytes)
    {
        var text = new UTF8Encoding(false).GetString(
            bytes.AsSpan(Encoding.UTF8.GetPreamble().Length));
        var rows = new List<IReadOnlyList<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character != '"')
                {
                    field.Append(character);
                }
                else if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r' when index + 1 < text.Length && text[index + 1] == '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add(fields.ToArray());
                    fields.Clear();
                    index++;
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        // The header row is not a record.
        return rows.Skip(1).ToArray();
    }

    private static async Task<ReportingFixture> SeedAsync(
        AsyncServiceScope scope,
        ApplicationDbContext database,
        RegistryAccessContext access)
    {
        var endpointService = scope.ServiceProvider.GetRequiredService<IEndpointRegistryService>();
        var environment = await database.Environments.AsNoTracking()
            .Include(candidate => candidate.Website)
            .Where(candidate => candidate.DeletedAt == null)
            .FirstAsync();

        var created = await endpointService.CreateAsync(
            new(environment.Id, "https://reporting.test/status", null, true, null,
                TargetAuthorizationKinds.Owned, "Reporting fixture owned by the project.", null),
            access);
        created.Succeeded.Should().BeTrue();
        var endpointId = created.EntityId!.Value;

        var monitors = await database.EndpointMonitors.AsNoTracking()
            .Where(monitor => monitor.EndpointId == endpointId && monitor.DeletedAt == null)
            .ToArrayAsync();
        var httpMonitorId = monitors
            .Single(monitor => monitor.MonitorType == RegistryDefaults.HttpAvailabilityMonitorType).Id;
        var sslMonitorId = monitors
            .Single(monitor => monitor.MonitorType == RegistryDefaults.SslCertificateMonitorType).Id;

        // Eligible availability samples, including one on each window boundary (BR-U04).
        await AddResultAsync(database, httpMonitorId, WindowStart, "Healthy", 100, countsForUptime: true);
        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(1), "Healthy", 200, true);
        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(2), "Healthy", 300, true);
        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(3), "Healthy", 400, true);
        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(4), "Warning", 1_800, true);
        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(5), "Critical", 15_000, true);
        await AddResultAsync(database, httpMonitorId, WindowEnd, "Healthy", 120, true);

        // Ineligible samples: a manual run and a maintenance-suppressed run.
        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(1).AddHours(1), "Healthy", 90, false);
        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(2).AddHours(1), "Critical", 9_000, false);

        // A certificate result, which is never an availability sample.
        await AddResultAsync(database, sslMonitorId, WindowStart.AddDays(1), "Healthy", 40, false);

        await database.SaveChangesAsync();

        return new(
            environment.Website.ClientId,
            environment.WebsiteId,
            environment.Id,
            environment.Website.OwnerSubjectId,
            endpointId,
            httpMonitorId,
            sslMonitorId);
    }

    private static async Task AddResultAsync(
        ApplicationDbContext database,
        Guid endpointMonitorId,
        DateTimeOffset measuredAt,
        string outcome,
        int totalDurationMs,
        bool countsForUptime)
    {
        var monitor = await database.EndpointMonitors.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == endpointMonitorId);
        var check = new LogicalCheck
        {
            Id = Guid.NewGuid(),
            EndpointMonitorId = endpointMonitorId,
            Source = LogicalCheckSources.Scheduled,
            ScheduledFor = measuredAt,
            // A scheduled check needs a cadence key, and it is unique per monitor: the
            // measurement instant is exactly the slot identity the scheduler would have used.
            CadenceKey = measuredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            State = LogicalCheckStates.Completed,
            PolicyFingerprint = monitor.ConfigurationFingerprint,
            CreatedAt = measuredAt,
            QueuedAt = measuredAt,
            StartedAt = measuredAt,
            CompletedAt = measuredAt
        };
        database.LogicalChecks.Add(check);
        database.CheckConfigurationSnapshots.Add(new CheckConfigurationSnapshot
        {
            LogicalCheckId = check.Id,
            SchemaVersion = 1,
            MonitorType = monitor.MonitorType,
            ConfigurationFingerprint = monitor.ConfigurationFingerprint,
            IntervalSeconds = monitor.IntervalSeconds,
            TimeoutSeconds = monitor.TimeoutSeconds,
            FailureConfirmationCount = monitor.FailureConfirmationCount,
            RecoveryConfirmationCount = monitor.RecoveryConfirmationCount,
            WarningThresholdMs = monitor.WarningThresholdMs,
            CriticalThresholdMs = monitor.CriticalThresholdMs,
            IntervalSource = "EnvironmentDefault",
            TimeoutSource = "PolicyProfile",
            ConfirmationSource = "PolicyProfile",
            ThresholdSource = "PolicyProfile",
            CreatedAt = measuredAt
        });
        database.CheckResults.Add(new CheckResult
        {
            LogicalCheckId = check.Id,
            EndpointMonitorId = endpointMonitorId,
            Outcome = outcome,
            FailureCategory = outcome == "Healthy" ? null : "ServerError",
            TotalDurationMs = totalDurationMs,
            MonitorSource = monitor.MonitorType == RegistryDefaults.SslCertificateMonitorType
                ? "WebHealthSslProbeV1"
                : "WebHealthSafeHttpV1",
            MeasuredAt = measuredAt,
            CountsForUptime = countsForUptime,
            CompletedAt = measuredAt
        });
    }

    private sealed record ReportingFixture(
        Guid ClientId,
        Guid WebsiteId,
        Guid EnvironmentId,
        Guid OwnerSubjectId,
        Guid EndpointId,
        Guid HttpMonitorId,
        Guid SslMonitorId);
}
