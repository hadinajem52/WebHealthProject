using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Middleware;
using WebHealth.Application.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using WebHealth.Web.Authorization;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Notifications;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "WebHealth")
    .WriteTo.Console());

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live", "ready"]);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IAuthorizationMiddlewareResultHandler, AuditingAuthorizationMiddlewareResultHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.Administration, policy =>
        policy.RequireRole(ApplicationRoles.Administrator))
    .AddPolicy(AuthorizationPolicies.Diagnostics, policy =>
        policy.RequireRole(ApplicationRoles.Administrator, ApplicationRoles.Operations))
    .AddPolicy(AuthorizationPolicies.OperateMonitoring, policy =>
        policy.RequireRole(ApplicationRoles.Administrator, ApplicationRoles.Operations))
    .AddPolicy(AuthorizationPolicies.ReadAllOperationalData, policy =>
        policy.RequireRole(
            ApplicationRoles.Administrator,
            ApplicationRoles.Operations))
    .AddPolicy(AuthorizationPolicies.ViewAuditHistory, policy =>
        policy.RequireRole(
            ApplicationRoles.Administrator,
            ApplicationRoles.Operations))
    .AddPolicy(AuthorizationPolicies.ReadRegistry, policy =>
        policy.RequireRole(ApplicationRoles.All.Select(role => role.Name).ToArray()))
    .AddPolicy(AuthorizationPolicies.ManageRegistry, policy =>
        policy.RequireRole(ApplicationRoles.Administrator, ApplicationRoles.Operations))
    .AddPolicy(AuthorizationPolicies.TestRegistryTargets, policy =>
        policy.RequireRole(
            ApplicationRoles.Administrator,
            ApplicationRoles.Operations,
            ApplicationRoles.DeveloperSupport))
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.LoginPath = "/Account/Login";
    options.SlidingExpiration = true;
});
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(5));

var app = builder.Build();

if (args.Contains("--bootstrap-admin", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>()
        .BootstrapAsync();
    return;
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    ExceptionHandlingPath = "/Home/Error",
    SuppressDiagnosticsCallback = _ => true
});
app.UseMiddleware<SafeExceptionLoggingMiddleware>();
app.UseStatusCodePagesWithReExecute("/Home/HttpStatusCode", "?code={0}");

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
});

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).RequireAuthorization(AuthorizationPolicies.Diagnostics);
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.UseMonitoringScheduling();
app.UseNotificationScheduling();

app.Run();

public partial class Program;
