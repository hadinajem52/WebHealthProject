using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Identity;
using Npgsql;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.IntegrationTests.Support;

internal static class DatabaseFoundationAssertions
{
    private static readonly string[] ExpectedIdentityTables =
    [
        "app_role",
        "app_role_claim",
        "app_user",
        "app_user_claim",
        "app_user_login",
        "app_user_role",
        "app_user_token"
    ];

    public static async Task VerifyAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString);
        await using var context = new ApplicationDbContext(options.Options);

        await context.Database.MigrateAsync();

        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await context.Database.GetAppliedMigrationsAsync()).Should().HaveCount(2);
        context.Model.GetEntityTypes().Should().HaveCount(7);

        var state = await ReadFoundationState(connectionString);
        state.SchemaExists.Should().BeTrue();
        state.Tables.Should().BeEquivalentTo(
            ExpectedIdentityTables.Append(DatabaseConventions.MigrationsHistoryTable));

        await VerifyIdentityBootstrapAsync(connectionString);
    }

    private static async Task VerifyIdentityBootstrapAsync(string connectionString)
    {
        var password = $"Integration-9!{Guid.NewGuid():N}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WebHealth"] = connectionString,
                ["BootstrapAdmin:Email"] = "bootstrap@example.test",
                ["BootstrapAdmin:DisplayName"] = "Bootstrap Administrator",
                ["BootstrapAdmin:Password"] = password
            })
            .Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();
            await bootstrapper.BootstrapAsync();
            await bootstrapper.BootstrapAsync();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var roles = await roleManager.Roles.OrderBy(role => role.Name).ToListAsync();
            roles.Select(role => (role.Id, role.Name)).Should().BeEquivalentTo(
                ApplicationRoles.All.Select(role => (role.Id, (string?)role.Name)));

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync("bootstrap@example.test");
            user.Should().NotBeNull();
            user!.PasswordHash.Should().NotBeNullOrWhiteSpace().And.NotBe(password);
            (await userManager.CheckPasswordAsync(user, password)).Should().BeTrue();
            (await userManager.IsInRoleAsync(user, ApplicationRoles.Administrator)).Should().BeTrue();

            var signInManager = scope.ServiceProvider
                .GetRequiredService<SignInManager<ApplicationUser>>();
            user.IsDisabled = true;
            (await signInManager.CanSignInAsync(user)).Should().BeFalse();
        }
    }

    private static async Task<(bool SchemaExists, IReadOnlyList<string> Tables)> ReadFoundationState(
        string connectionString)
    {
        const string sql = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'web_health'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return (tables.Count > 0, tables);
    }
}
