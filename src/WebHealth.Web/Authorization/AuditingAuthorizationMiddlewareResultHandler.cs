using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using WebHealth.Application.Auditing;

namespace WebHealth.Web.Authorization;

public sealed class AuditingAuthorizationMiddlewareResultHandler(
    IAuthorizationDenialAuditWriter auditWriter,
    ILogger<AuditingAuthorizationMiddlewareResultHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
        {
            await WriteDenialAsync(context);
        }

        await defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    private async Task WriteDenialAsync(HttpContext context)
    {
        try
        {
            var actorClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var actorUserId = Guid.TryParse(actorClaim, out var parsedUserId) ? (Guid?)parsedUserId : null;
            await auditWriter.WriteAsync(
                new AuthorizationDenialAuditEntry(
                    actorUserId,
                    DateTimeOffset.UtcNow,
                    Limit(context.Request.Method, 16),
                    Limit($"{context.Request.PathBase}{context.Request.Path}", 2048),
                    Limit(context.TraceIdentifier, 128)),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to persist authorization denial for {RequestMethod} {RequestPath}",
                context.Request.Method,
                context.Request.Path);
        }
    }

    private static string Limit(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
