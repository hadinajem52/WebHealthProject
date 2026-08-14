using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Infrastructure.Diagnostics;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Application.Administration;
using WebHealth.Application.Auditing;
using WebHealth.Infrastructure.Auditing;
using WebHealth.Application.Assignments;
using WebHealth.Infrastructure.Assignments;

namespace WebHealth.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "WebHealth";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString(DatabaseConnectionName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{DatabaseConnectionName} is not configured.");
            }

            PostgreSqlDbContextOptions.Configure(options, connectionString);
        });

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager<ApplicationUserSignInManager>()
            .AddDefaultTokenProviders();
        services.Configure<BootstrapAdminOptions>(
            configuration.GetSection(BootstrapAdminOptions.SectionName));
        services.AddScoped<AdminBootstrapper>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<ITeamAdministrationService, TeamAdministrationService>();
        services.AddScoped<IAssignmentAccessEvaluator, AssignmentAccessEvaluator>();
        services.AddScoped<IAuditTrailWriter, AuditTrailWriter>();
        services.AddScoped<IAuditTrailReader, AuditTrailReader>();
        services.AddScoped<IAuthorizationDenialAuditWriter, AuthorizationDenialAuditWriter>();

        services.AddHealthChecks()
            .AddCheck<PostgreSqlReadinessCheck>("postgresql", tags: ["ready"]);

        return services;
    }
}
