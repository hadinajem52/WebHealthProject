using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.PageAudits;
using WebHealth.Infrastructure.Identity;
using WebHealth.Web.Middleware;
using WebHealth.Application.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using WebHealth.Web.Authorization;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Infrastructure.Maintenance;
using WebHealth.Infrastructure.Notifications;
using WebHealth.Infrastructure.Seo;
using WebHealth.Infrastructure.Crawling;
using WebHealth.Infrastructure.PngAudits;
using WebHealth.Web.Ajax;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "WebHealth")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"));

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());

    var messages = options.ModelBindingMessageProvider;
    messages.SetValueIsInvalidAccessor(value =>
        $"{value} is not a value this field accepts.");
    messages.SetAttemptedValueIsInvalidAccessor((value, field) =>
        $"{field} does not accept {value}. Enter a value of the right kind.");
    messages.SetValueMustNotBeNullAccessor(_ => "This field is required.");
    messages.SetMissingBindRequiredValueAccessor(field =>
        $"{field} was missing from the request. Reload the page and submit the form again.");
    messages.SetMissingKeyOrValueAccessor(() =>
        "A required value was missing. Reload the page and submit the form again.");
    messages.SetMissingRequestBodyRequiredValueAccessor(() =>
        "The request arrived empty. Reload the page and submit the form again.");
    messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value =>
        $"{value} is not a value this field accepts.");
    messages.SetNonPropertyValueMustBeANumberAccessor(() => "Enter a number.");
    messages.SetValueMustBeANumberAccessor(field => $"{field} must be a number.");
    messages.SetUnknownValueIsInvalidAccessor(field =>
        $"{field} does not accept that value.");
    messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => "That value is not accepted here.");
});
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
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.IsWebHealthAjax())
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.IsWebHealthAjax())
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
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

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
});
app.UseMiddleware<SafeExceptionLoggingMiddleware>();
app.UseWhen(
    context => !context.Request.IsWebHealthAjax(),
    branch => branch.UseStatusCodePagesWithReExecute("/Home/HttpStatusCode", "?code={0}"));

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
app.UseMonitoringRetention();
app.UseNotificationScheduling();
app.UseMaintenanceScheduling();
app.UseSeoScheduling();
app.UsePageAuditScheduling();
app.UseCrawlScheduling();
app.UsePngAuditScheduling();

app.Run();

public partial class Program;
