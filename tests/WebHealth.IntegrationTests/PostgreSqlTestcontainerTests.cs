using Testcontainers.PostgreSql;
using WebHealth.IntegrationTests.Support;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class PostgreSqlTestcontainerTests
{
    [TestcontainersFact]
    public async Task CurrentMigrations_ApplyToPostgreSqlContainer()
    {
        await using var postgreSql = new PostgreSqlBuilder("postgres:18.0-bookworm").Build();

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
