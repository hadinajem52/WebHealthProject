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
using WebHealth.Infrastructure.Incidents;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.IntegrationTests.Support;

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
        await VerifyScreenAndCsvSelectTheSameRecordsAsync(services, access, fixture);
        await VerifyIncidentListAndCountDescribeOneSelectionAsync(reader, access, fixture);
        await VerifyAPageBeyondTheEndReportsThePageItServedAsync(reader, access, fixture);
        await VerifyVisibilityIsAppliedToBothSurfacesAsync(services, database, reader, fixture);
    }

    private static async Task VerifyUptimeCountsOnlyEligibleSamplesAsync(
        IReportingReader reader,
        RegistryAccessContext access,
        ReportingFixture fixture)
    {
        var query = Query(fixture, monitorType: null);
        var rows = await ReadEveryScreenPageAsync(reader, query, access);
        var row = rows.Single(candidate => candidate.EndpointMonitorId == fixture.HttpMonitorId);

        row.Uptime.EligibleSamples.Should().Be(6);
        row.Uptime.HealthySamples.Should().Be(4);
        row.Uptime.WarningSamples.Should().Be(1);
        row.Uptime.DownSamples.Should().Be(1);
        row.Uptime.ExcludedSamples.Should().Be(2);

        row.Uptime.Percentage.Should().BeApproximately(83.3333, 0.001);
        row.Uptime.CleanPercentage.Should().BeApproximately(66.6667, 0.001);
        (row.Uptime.HealthySamples + row.Uptime.WarningSamples + row.Uptime.DownSamples)
            .Should().Be(row.Uptime.EligibleSamples, "every eligible sample lands in exactly one category");

        var certificateRow = rows.Single(candidate =>
            candidate.EndpointMonitorId == fixture.SslMonitorId);
        certificateRow.Uptime.EligibleSamples.Should().Be(0);
        certificateRow.Uptime.Percentage.Should().BeNull();
        certificateRow.Uptime.ExcludedSamples.Should().BeGreaterThan(0);
    }

    private static async Task VerifyWindowIsHalfOpenAsync(
        IReportingReader reader,
        RegistryAccessContext access,
        ReportingFixture fixture)
    {
        var rows = await ReadEveryScreenPageAsync(reader, Query(fixture, monitorType: null), access);
        var row = rows.Single(candidate => candidate.EndpointMonitorId == fixture.HttpMonitorId);

        var nextPeriod = await ReadEveryScreenPageAsync(
            reader,
            Query(fixture, monitorType: null, start: WindowEnd, end: WindowEnd.AddDays(7)),
            access);
        var nextRow = nextPeriod.Single(candidate => candidate.EndpointMonitorId == fixture.HttpMonitorId);

        row.Uptime.EligibleSamples.Should().Be(6);
        nextRow.Uptime.EligibleSamples.Should().Be(1);
        nextRow.LastMeasuredAt.Should().Be(WindowEnd);
    }

    private static async Task VerifyPercentilesUseSuccessfulSamplesOnlyAsync(
        IReportingReader reader,
        RegistryAccessContext access,
        ReportingFixture fixture)
    {
        var rows = await ReadEveryScreenPageAsync(reader, Query(fixture, monitorType: null), access);
        var row = rows.Single(candidate => candidate.EndpointMonitorId == fixture.HttpMonitorId);

        row.ResponseTimes.MeasuredSamples.Should().Be(5);

        row.ResponseTimes.P50Ms.Should().Be(300);
        row.ResponseTimes.P95Ms.Should().BeApproximately(1_520, 0.001);

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
        dataset.Trend.Sum(point => point.UpSamples)
            .Should().Be(dataset.Summary.Uptime.HealthySamples + dataset.Summary.Uptime.WarningSamples);
        dataset.Trend.Should().OnlyContain(point =>
            point.Day >= DateOnly.FromDateTime(WindowStart.UtcDateTime)
            && point.Day < DateOnly.FromDateTime(WindowEnd.UtcDateTime));

        var timeoutDay = dataset.Trend.Single(point =>
            point.Day == DateOnly.FromDateTime(WindowStart.AddDays(5).UtcDateTime));
        timeoutDay.UpSamples.Should().Be(0);
        timeoutDay.P95Ms.Should().BeNull("the only sample that day was a failed exchange");
    }

    private static async Task VerifyIncidentListAndCountDescribeOneSelectionAsync(
        IReportingReader reader,
        RegistryAccessContext access,
        ReportingFixture fixture)
    {
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

    private static async Task VerifyScreenAndCsvSelectTheSameRecordsAsync(
        ServiceProvider services,
        RegistryAccessContext access,
        ReportingFixture fixture)
    {
        var covered = 0;
        await ForEachReaderAsync(services, EveryFilterCombination(fixture), async (reader, query) =>
        {
            var screenRows = await ReadEveryScreenPageAsync(reader, query, access);

            var export = await reader.ExportAsync(query, access);

            export.Query.Page.Should().Be(1);
            export.TotalCount.Should().Be(screenRows.Count);

            export.Rows.Should().Equal(
                screenRows,
                "the screen and the export are one query at two page sizes");

            var csvRows = ParseCsv(ReportCsv.Write(export));
            csvRows.Should().HaveCount(screenRows.Count);
            csvRows.Select(row => row[0])
                .Should().Equal(screenRows.Select(row => row.EndpointMonitorId.ToString()));
            csvRows.Select(row => row[Array.IndexOf(ReportCsv.Headers.ToArray(), "UptimePercent")])
                .Should().Equal(screenRows.Select(row => Rendered(row.Uptime.Percentage)));
            csvRows.Select(row => row[Array.IndexOf(ReportCsv.Headers.ToArray(), "OperationalState")])
                .Should().Equal(screenRows.Select(row => row.Operation!.State));
            csvRows.Select(row => row[Array.IndexOf(ReportCsv.Headers.ToArray(), "ConfirmedStatus")])
                .Should().Equal(screenRows.Select(row => row.ConfirmedStatus));

            Interlocked.Increment(ref covered);
        });

        covered.Should().BeGreaterThan(50);
    }

    private static string Rendered(double? value) =>
        value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;

    private static async Task VerifyVisibilityIsAppliedToBothSurfacesAsync(
        ServiceProvider services,
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
        (await reader.QueryDiagnosticsAsync(query, unprivileged)).Runtime.Should().BeNull(
            "Viewer output excludes detailed scheduler and worker runtime evidence");

        await ForEachReaderAsync(services, EveryScopedFilter(fixture), async (scopedReader, scopedQuery) =>
        {
            (await scopedReader.QueryAsync(scopedQuery, unprivileged)).Rows.Should().BeEmpty();
            (await scopedReader.ExportAsync(scopedQuery, unprivileged)).Rows.Should().BeEmpty();
            (await scopedReader.QueryCertificateExpiryAsync(scopedQuery, unprivileged))
                .NeedingAttention.Should().BeEmpty();
            (await scopedReader.QueryDiagnosticsAsync(scopedQuery, unprivileged))
                .ScheduledMonitorCount.Should().Be(0);
            (await scopedReader.QueryActiveIncidentsAsync(scopedQuery, unprivileged, 10)).Should().BeEmpty();
        });
    }

    private static Task ForEachReaderAsync<T>(
        ServiceProvider services,
        IEnumerable<T> source,
        Func<IReportingReader, T, Task> body) =>
        Parallel.ForEachAsync(
            source,
            new ParallelOptions { MaxDegreeOfParallelism = 6 },
            async (item, _) =>
            {
                await using var scope = services.CreateAsyncScope();
                await body(scope.ServiceProvider.GetRequiredService<IReportingReader>(), item);
            });

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
            .Where(candidate => candidate.DeletedAt == null
                && candidate.IsActive
                && candidate.Website.DeletedAt == null)
            .OrderBy(candidate => candidate.CreatedAt)
            .FirstAsync();

        var created = await endpointService.CreateAsync(
            new(environment.Id, "https://reporting.test/status", null, true, null),
            access);
        created.Succeeded.Should().BeTrue(string.Join(" ", created.Errors));
        var endpointId = created.EntityId!.Value;

        var monitors = await database.EndpointMonitors.AsNoTracking()
            .Where(monitor => monitor.EndpointId == endpointId && monitor.DeletedAt == null)
            .ToArrayAsync();
        var httpMonitorId = monitors
            .Single(monitor => monitor.MonitorType == RegistryDefaults.HttpAvailabilityMonitorType).Id;
        var sslMonitorId = monitors
            .Single(monitor => monitor.MonitorType == RegistryDefaults.SslCertificateMonitorType).Id;

        await AddResultAsync(database, httpMonitorId, WindowStart, "Healthy", 100, countsForUptime: true);
        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(1), "Healthy", 200, true);
        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(2), "Healthy", 300, true);
        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(3), "Healthy", 400, true);
        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(4), "Warning", 1_800, true);
        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(5), "Critical", 15_000, true);
        await AddResultAsync(database, httpMonitorId, WindowEnd, "Healthy", 120, true);

        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(1).AddHours(1), "Healthy", 90, false);
        await AddResultAsync(database, httpMonitorId, WindowStart.AddDays(2).AddHours(1), "Critical", 9_000, false);

        await AddResultAsync(database, sslMonitorId, WindowStart.AddDays(1), "Healthy", 40, false);

        var ownerSubjectId = await database.OwnerSubjects.AsNoTracking()
            .OrderBy(subject => subject.Id)
            .Select(subject => subject.Id)
            .FirstAsync();
        database.Incidents.Add(new Incident
        {
            Id = Guid.NewGuid(),
            EndpointMonitorId = httpMonitorId,
            OwnerSubjectId = ownerSubjectId,
            IssueKey = HttpIssueIdentity.Create("Http.ServerError"),
            Severity = IncidentSeverities.Critical,
            Status = IncidentStatuses.Open,
            OpenedAt = WindowStart.AddDays(3),
            Version = 1
        });

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
            FailureCategory = outcome switch
            {
                "Healthy" => null,
                "Warning" => HttpFailureCategories.SlowResponse,
                _ => "ServerError"
            },
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
