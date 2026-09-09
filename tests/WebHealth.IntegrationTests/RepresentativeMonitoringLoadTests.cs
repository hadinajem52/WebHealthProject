using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class RepresentativeMonitoringLoadTests
{
    [RepresentativeMonitoringFact]
    public async Task ControlledFleet_MeetsSchedulingRecoveryConcurrencyAndMemoryGoals()
    {
        await RepresentativeMonitoringLoad.VerifyAsync(
            Environment.GetEnvironmentVariable("WEBHEALTH_REPRESENTATIVE_CONNECTION")!,
            Environment.GetEnvironmentVariable("WEBHEALTH_REPRESENTATIVE_EVIDENCE")!,
            int.Parse(Environment.GetEnvironmentVariable("WEBHEALTH_MEMORY_WINDOW_MINUTES")!));
    }

    [RepresentativeMonitoringRestartFact]
    public async Task RestartedPostgresql_ReconcilesTheOutstandingFleetWithoutRepair()
    {
        await RepresentativeMonitoringLoad.VerifyDatabaseRestartRecoveryAsync(
            Environment.GetEnvironmentVariable("WEBHEALTH_REPRESENTATIVE_CONNECTION")!,
            Environment.GetEnvironmentVariable("WEBHEALTH_REPRESENTATIVE_EVIDENCE")!);
    }
}

public sealed class RepresentativeMonitoringRestartFactAttribute : FactAttribute
{
    public RepresentativeMonitoringRestartFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("WEBHEALTH_POSTGRESQL_OUTAGE_OBSERVED"), "true",
                StringComparison.Ordinal))
        {
            Skip = "Run scripts/run-representative-monitoring-evidence.ps1 to enable this test.";
        }
        Timeout = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
    }
}

public sealed class RepresentativeMonitoringFactAttribute : FactAttribute
{
    public RepresentativeMonitoringFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBHEALTH_REPRESENTATIVE_CONNECTION"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBHEALTH_REPRESENTATIVE_EVIDENCE"))
            || !int.TryParse(Environment.GetEnvironmentVariable("WEBHEALTH_MEMORY_WINDOW_MINUTES"), out _))
        {
            Skip = "Run scripts/run-representative-monitoring-evidence.ps1 to enable this test.";
        }
        Timeout = (int)TimeSpan.FromMinutes(40).TotalMilliseconds;
    }
}
