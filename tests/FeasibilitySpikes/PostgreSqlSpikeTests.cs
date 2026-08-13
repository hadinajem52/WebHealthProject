using Hangfire;
using Hangfire.Common;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FeasibilitySpikes;

public sealed class PostgreSqlSpikeTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("SPIKE_POSTGRES")
        ?? throw new InvalidOperationException("Run tests through scripts/run-feasibility-spikes.ps1.");

    [Fact]
    public async Task EfCoreAndHangfirePersistJobsAndIsolateQueuesAcrossWorkers()
    {
        await ResetDatabase();

        await using (var context = new SpikeDbContext(ConnectionString))
        {
            Assert.True(await context.Database.CanConnectAsync());
            Assert.Equal(18, await context.Database.SqlQueryRaw<int>("select current_setting('server_version_num')::int / 10000 as \"Value\"").SingleAsync());
        }

        var storage = CreateStorage();
        var client = new BackgroundJobClient(storage);
        SpikeJob.ConnectionString = ConnectionString;

        client.Create(Job.FromExpression(() => SpikeJob.Record("persisted")), new EnqueuedState("alpha"));
        client.Create(Job.FromExpression(() => SpikeJob.Record("isolated")), new EnqueuedState("beta"));

        // Recreate storage before workers start to prove jobs survive process-style restart boundaries.
        var restartedStorage = CreateStorage();
        using (var alphaWorker = CreateWorker(restartedStorage, "alpha"))
        {
            await WaitForCount("persisted", 1);
            await Task.Delay(750);
            Assert.Equal(0, await Count("isolated"));
        }

        using (var betaWorker = CreateWorker(restartedStorage, "beta"))
        {
            await WaitForCount("isolated", 1);
        }
    }

    [Fact]
    public async Task CompetingTransactionsEnforceLeaseAndDeduplicationInvariants()
    {
        await ResetDatabase();
        await ExecuteSchema();

        Assert.Equal(1, await RaceInserts("insert into check_result(logical_check_id) values ('11111111-1111-1111-1111-111111111111')"));
        Assert.Equal(1, await RaceInserts("insert into incident(endpoint_monitor_id, issue_key, status) values ('22222222-2222-2222-2222-222222222222', 'http|status', 'Open')"));
        Assert.Equal(1, await RaceInserts("insert into notification_delivery(notification_event_id, channel, normalized_recipient) values ('33333333-3333-3333-3333-333333333333', 'Email', 'owner@example.test')"));

        var leaseWinners = await Task.WhenAll(AcquireLease("worker-a"), AcquireLease("worker-b"));
        Assert.Equal(1, leaseWinners.Count(winner => winner));

        await using var connection = await OpenConnection();
        var initialGeneration = await Scalar<long>(connection, "select generation from execution_lease where lease_key = 'monitor:1'");
        await Execute(connection, "update execution_lease set expires_at = clock_timestamp() - interval '1 second'");
        Assert.True(await AcquireLease("worker-c"));
        var recoveredGeneration = await Scalar<long>(connection, "select generation from execution_lease where lease_key = 'monitor:1'");
        Assert.Equal(initialGeneration + 1, recoveredGeneration);

        var staleWrite = await Execute(connection, "update execution_lease set expires_at = clock_timestamp() + interval '1 minute' where lease_key = 'monitor:1' and generation = @generation", new NpgsqlParameter("generation", initialGeneration));
        Assert.Equal(0, staleWrite);
    }

    private static PostgreSqlStorage CreateStorage()
    {
        var options = new PostgreSqlStorageOptions
        {
            SchemaName = "hangfire_spike",
            PrepareSchemaIfNecessary = true,
            QueuePollInterval = TimeSpan.FromMilliseconds(100)
        };
        return new PostgreSqlStorage(new NpgsqlConnectionFactory(ConnectionString, options, null), options);
    }

    private static BackgroundJobServer CreateWorker(JobStorage storage, string queue) => new(
        new BackgroundJobServerOptions
        {
            Queues = [queue],
            WorkerCount = 1,
            SchedulePollingInterval = TimeSpan.FromMilliseconds(100)
        },
        storage);

    private static async Task ResetDatabase()
    {
        await using var connection = await OpenConnection();
        await Execute(connection, "drop schema if exists hangfire_spike cascade; drop table if exists spike_job, execution_lease, check_result, incident, notification_delivery cascade; create table spike_job(value text not null)");
    }

    private static async Task ExecuteSchema()
    {
        await using var connection = await OpenConnection();
        await Execute(connection, """
            create table execution_lease (
                lease_key text primary key,
                owner_token text not null,
                generation bigint not null,
                expires_at timestamptz not null
            );
            create table check_result (logical_check_id uuid primary key);
            create table incident (
                id bigint generated always as identity primary key,
                endpoint_monitor_id uuid not null,
                issue_key text not null,
                status text not null
            );
            create unique index ux_incident_active on incident(endpoint_monitor_id, issue_key)
                where status in ('Open', 'Acknowledged', 'InProgress', 'MonitoringRecovery');
            create table notification_delivery (
                id bigint generated always as identity primary key,
                notification_event_id uuid not null,
                channel text not null,
                normalized_recipient text not null,
                unique(notification_event_id, channel, normalized_recipient)
            );
            """);
    }

    private static async Task<int> RaceInserts(string sql)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new CountdownEvent(2);

        async Task<bool> Attempt()
        {
            await using var connection = await OpenConnection();
            ready.Signal();
            await gate.Task;
            try
            {
                await Execute(connection, sql);
                return true;
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return false;
            }
        }

        var attempts = new[] { Attempt(), Attempt() };
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        gate.SetResult();
        return (await Task.WhenAll(attempts)).Count(success => success);
    }

    private static async Task<bool> AcquireLease(string owner)
    {
        await using var connection = await OpenConnection();
        await using var command = new NpgsqlCommand("""
            insert into execution_lease(lease_key, owner_token, generation, expires_at)
            values ('monitor:1', @owner, 1, clock_timestamp() + interval '30 seconds')
            on conflict (lease_key) do update
            set owner_token = excluded.owner_token,
                generation = execution_lease.generation + 1,
                expires_at = excluded.expires_at
            where execution_lease.expires_at <= clock_timestamp()
            returning generation
            """, connection);
        command.Parameters.AddWithValue("owner", owner);
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task WaitForCount(string value, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await Count(value) == expected)
            {
                return;
            }
            await Task.Delay(100);
        }
        Assert.Fail($"Timed out waiting for job '{value}'.");
    }

    private static async Task<int> Count(string value)
    {
        await using var connection = await OpenConnection();
        await using var command = new NpgsqlCommand("select count(*)::int from spike_job where value = @value", connection);
        command.Parameters.AddWithValue("value", value);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<NpgsqlConnection> OpenConnection()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<int> Execute(NpgsqlConnection connection, string sql, params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private sealed class SpikeDbContext(string connectionString) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseNpgsql(connectionString);
    }
}

public static class SpikeJob
{
    public static string ConnectionString { private get; set; } = string.Empty;

    public static void Record(string value)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand("insert into spike_job(value) values (@value)", connection);
        command.Parameters.AddWithValue("value", value);
        command.ExecuteNonQuery();
    }
}
