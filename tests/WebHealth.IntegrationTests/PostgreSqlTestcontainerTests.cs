using Testcontainers.PostgreSql;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class PostgreSqlTestcontainerTests
{
    [TestcontainersFact]
    public async Task FoundationMigration_AppliesToPostgreSqlContainer()
    {
        await using var postgreSql = new PostgreSqlBuilder("postgres:18.0-bookworm")
            .WithDatabase("webhealth_tests")
            .WithUsername("webhealth")
            .WithPassword("webhealth_tests_only")
            .Build();

        await postgreSql.StartAsync();

        await DatabaseFoundationAssertions.VerifyAsync(postgreSql.GetConnectionString());
    }
}

public sealed class TestcontainersFactAttribute : FactAttribute
{
    public TestcontainersFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("WEBHEALTH_TESTCONTAINERS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set WEBHEALTH_TESTCONTAINERS=true when a Docker engine is available.";
        }
    }
}
