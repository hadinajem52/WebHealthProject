using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebHealth.Infrastructure.Persistence;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class DatabaseConventionTests
{
    [Fact]
    public void Model_UsesRecordedPostgreSqlConventions()
    {
        using var context = CreateConventionContext();
        var parent = context.Model.FindEntityType(typeof(ConventionParent))!;
        var child = context.Model.FindEntityType(typeof(ConventionChild))!;

        Assert.Equal(DatabaseConventions.DefaultSchema, context.Model.GetDefaultSchema());
        Assert.Equal("convention_parent", parent.GetTableName());
        Assert.Equal("display_name", parent.FindProperty(nameof(ConventionParent.DisplayName))!.GetColumnName());
        Assert.Equal(
            DatabaseConventions.TimestampWithTimeZone,
            parent.FindProperty(nameof(ConventionParent.CreatedAt))!.GetColumnType());
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(child.GetForeignKeys()).DeleteBehavior);
        Assert.Equal("pk_convention_parent", parent.FindPrimaryKey()!.GetName());
        Assert.Equal("ix_convention_parent_display_name", Assert.Single(parent.GetIndexes()).GetDatabaseName());
    }

    [Theory]
    [InlineData("DisplayName", "display_name")]
    [InlineData("URLHash", "url_hash")]
    [InlineData("IPAddress", "ip_address")]
    public void SnakeCaseConverter_IsDeterministic(string input, string expected)
    {
        Assert.Equal(expected, DatabaseConventions.ToSnakeCase(input));
    }

    private static ConventionDbContext CreateConventionContext()
    {
        var options = new DbContextOptionsBuilder<ConventionDbContext>()
            .UseNpgsql("Host=localhost;Database=convention_model;Username=webhealth")
            .Options;
        return new ConventionDbContext(options);
    }

    private sealed class ConventionDbContext(DbContextOptions<ConventionDbContext> options)
        : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DatabaseConventions.Configure(configurationBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ConventionParent>(entity =>
            {
                entity.HasKey(parent => parent.Id);
                entity.HasIndex(parent => parent.DisplayName);
            });
            modelBuilder.Entity<ConventionChild>(entity =>
            {
                entity.HasKey(child => child.Id);
                entity.HasOne<ConventionParent>()
                    .WithMany()
                    .HasForeignKey(child => child.ParentId);
            });
            DatabaseConventions.Apply(modelBuilder);
        }
    }

    private sealed class ConventionParent
    {
        public Guid Id { get; init; }
        public required string DisplayName { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    private sealed class ConventionChild
    {
        public Guid Id { get; init; }
        public Guid ParentId { get; init; }
    }
}

public sealed class DatabaseFoundationTests
{
    private const string TestConnectionEnvironmentVariable = "WEBHEALTH_TEST_POSTGRES";

    [PostgreSqlFact]
    public async Task CleanDatabase_AppliesOnlyTheFoundationMigration()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestConnectionEnvironmentVariable)!;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString);
        await using var context = new ApplicationDbContext(options.Options);

        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(context.Model.GetEntityTypes());
        Assert.Equal((true, 1L), await ReadFoundationState(connectionString));
    }

    private static async Task<(bool SchemaExists, long TableCount)> ReadFoundationState(
        string connectionString)
    {
        const string sql = """
            SELECT
                to_regnamespace('web_health') IS NOT NULL,
                count(*)
            FROM information_schema.tables
            WHERE table_schema = 'web_health'
              AND table_type = 'BASE TABLE';
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetBoolean(0), reader.GetInt64(1));
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("WEBHEALTH_TEST_POSTGRES")))
        {
            Skip = "Run scripts/run-database-foundation-tests.ps1 to enable this test.";
        }
    }
}
