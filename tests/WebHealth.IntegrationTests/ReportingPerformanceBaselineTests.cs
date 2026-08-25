using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

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

        Timeout = (int)TimeSpan.FromMinutes(30).TotalMilliseconds;
    }
}
