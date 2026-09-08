namespace WebHealth.Application.Monitoring;

public sealed record MonitoringOperationStatus(string Operation, DateTimeOffset LastStartedAt,
    DateTimeOffset? LastSucceededAt, DateTimeOffset? LastFailedAt, long? LastDurationMs,
    string? FailureCategory, int ConsecutiveFailures);

public sealed record MonitoringWorkerStatus(bool Available, bool ServerPresent, bool QueueCovered, DateTimeOffset? LastHeartbeatAt);

public interface IMonitoringWorkerReader
{
    MonitoringWorkerStatus Read();
}

public sealed record MonitoringRuntimeDiagnostics(bool SchedulingEnabled, MonitoringWorkerStatus Workers,
    IReadOnlyList<MonitoringOperationStatus> Operations);

public sealed record MonitoringEngineHealth(string Status, IReadOnlyList<string> Reasons)
{
    public static MonitoringEngineHealth Evaluate(bool enabled, IReadOnlyList<MonitoringOperationStatus> operations,
        MonitoringWorkerStatus workers, DateTimeOffset? oldestQueuedAt, DateTimeOffset? oldestDueAt,
        TimeSpan dispatchDelayGrace, DateTimeOffset now)
    {
        if (!enabled) return new("Healthy", ["DisabledByConfiguration"]);
        var warning = new List<string>();
        var critical = new List<string>();
        foreach (var operation in new[] { "monitoring-dispatch", "monitoring-reconciliation" })
        {
            var runtime = operations.SingleOrDefault(item => item.Operation == operation);
            if (runtime?.LastSucceededAt is not { } success || now - success >= TimeSpan.FromMinutes(5))
                critical.Add($"{operation}:SuccessOverdue");
            else if (now - success >= TimeSpan.FromMinutes(3))
                warning.Add($"{operation}:SuccessDelayed");
            if (runtime?.ConsecutiveFailures >= 3) critical.Add($"{operation}:RepeatedFailure");
            else if (runtime?.ConsecutiveFailures > 0) warning.Add($"{operation}:Failed");
        }
        if (!workers.Available) critical.Add("WorkerStatusUnavailable");
        else if (!workers.ServerPresent) critical.Add("WorkerAbsent");
        else if (!workers.QueueCovered) critical.Add("ShortCheckQueueUncovered");
        else if (workers.LastHeartbeatAt is not { } heartbeat || now - heartbeat > TimeSpan.FromMinutes(2))
            critical.Add("WorkerHeartbeatStale");
        if (oldestQueuedAt is { } queued)
        {
            if (now - queued >= TimeSpan.FromMinutes(15)) critical.Add("QueueAgeCritical");
            else if (now - queued >= TimeSpan.FromMinutes(5)) warning.Add("QueueAgeWarning");
        }
        if (oldestDueAt is { } due)
        {
            if (now - due >= TimeSpan.FromMinutes(30)) critical.Add("DispatchOverdueCritical");
            else if (now - due > dispatchDelayGrace) warning.Add("DispatchOverdueWarning");
        }
        return new(critical.Count > 0 ? "Critical" : warning.Count > 0 ? "Warning" : "Healthy", [.. critical, .. warning]);
    }
}
