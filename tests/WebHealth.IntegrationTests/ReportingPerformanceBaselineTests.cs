using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

/// <summary>
/// Phase 5 increment 5.7. Seeds a representative fleet, captures the plan for every reporting
/// query, and measures the dashboard against NFR-02.
/// </summary>
/// <remarks>
/// This is deliberately not part of the database foundation gate. It writes roughly two million
/// samples and runs every scenario twenty times, which is minutes of work; folding it into the
/// correctness gate would make the gate too slow to run often, and a slow gate is one that stops
/// being run.
/// </remarks>
public sealed class ReportingPerformanceBaselineTests
{
    [ReportingBaselineFact]
    public async Task RepresentativeData_KeepsTheDashboardInsideItsBudget()
    {
        await ReportingPerformanceBaseline.VerifyAsync(
            Environment.GetEnvironmentVariable("WEBHEALTH_TEST_POSTGRES_BASELINE")!,
            Environment.GetEnvironmentVariable("WEBHEALTH_BASELINE_SERVER_LOG")!,
            Environment.GetEnvironmentVariable("WEBHEALTH_BASELINE_EVIDENCE")!);
    }
}

public sealed class ReportingBaselineFactAttribute : FactAttribute
{
    private static readonly string[] Required =
    [
        "WEBHEALTH_TEST_POSTGRES_BASELINE",
        "WEBHEALTH_BASELINE_SERVER_LOG",
        "WEBHEALTH_BASELINE_EVIDENCE"
    ];

    public ReportingBaselineFactAttribute()
    {
        if (Required.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "Run scripts/run-reporting-performance-baseline.ps1 to enable this test.";
        }

        // Seeding, plan capture and twenty timed iterations of six scenarios.
        Timeout = (int)TimeSpan.FromMinutes(30).TotalMilliseconds;
    }
}
