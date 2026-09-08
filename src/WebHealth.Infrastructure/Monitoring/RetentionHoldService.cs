using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Auditing;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class RetentionHoldService(ApplicationDbContext database, TimeProvider timeProvider) : IRetentionHoldService
{
    public async Task<IReadOnlyList<RetentionHoldView>> ListAsync(RegistryAccessContext access, int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (!await IsAdministratorAsync(access, cancellationToken))
            throw new UnauthorizedAccessException();
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return await database.RetentionHolds.AsNoTracking().OrderByDescending(hold => hold.CreatedAt)
            .ThenBy(hold => hold.Id).Skip(offset).Take(100)
            .Select(hold => new RetentionHoldView(hold.Id, hold.ScopeType, hold.ScopeId, hold.Reason,
                hold.CreatedByUserId, hold.CreatedAt, hold.ExpiresAt, hold.ReleasedByUserId, hold.ReleasedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<RegistryMutationResult> CreateAsync(CreateRetentionHold command, RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!await IsAdministratorAsync(access, cancellationToken))
            return RegistryMutationResult.Failure(RegistryMutationStatus.Forbidden);
        var reason = command.Reason?.Trim();
        if (reason is not { Length: > 0 and <= 500 })
            return RegistryMutationResult.Failure(RegistryMutationStatus.ValidationFailed, "Enter a reason of 1 to 500 characters.");
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await RetentionTransactionLock.AcquireAsync(database, cancellationToken);
        var now = UtcNow();
        var expiresAt = command.ExpiresAt is { } requestedExpiry
            ? requestedExpiry.ToUniversalTime().AddTicks(-(requestedExpiry.Ticks % 10)) : (DateTimeOffset?)null;
        if (expiresAt is { } expiry && expiry <= now)
            return RegistryMutationResult.Failure(RegistryMutationStatus.ValidationFailed, "Expiry must be later than creation.");
        if (!await ScopeExistsAsync(command.ScopeType, command.ScopeId, cancellationToken))
            return RegistryMutationResult.Failure(RegistryMutationStatus.ValidationFailed, "Select an existing supported scope.");
        var hold = new RetentionHold
        {
            Id = Guid.NewGuid(),
            ScopeType = command.ScopeType,
            ScopeId = command.ScopeId,
            Reason = reason,
            CreatedByUserId = access.UserId,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };
        database.RetentionHolds.Add(hold);
        AddAudit(hold, access.UserId, now, "retention.holdcreated", null);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RegistryMutationResult.Success(hold.Id);
    }

    public async Task<RegistryMutationResult> ReleaseAsync(Guid holdId, RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!await IsAdministratorAsync(access, cancellationToken))
            return RegistryMutationResult.Failure(RegistryMutationStatus.Forbidden);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await RetentionTransactionLock.AcquireAsync(database, cancellationToken);
        var hold = await database.RetentionHolds.SingleOrDefaultAsync(item => item.Id == holdId, cancellationToken);
        if (hold is null) return RegistryMutationResult.Failure(RegistryMutationStatus.NotFound);
        await database.Entry(hold).ReloadAsync(cancellationToken);
        if (hold.ReleasedAt is not null) return RegistryMutationResult.Success(hold.Id);
        var now = UtcNow();
        if (now < hold.CreatedAt)
            return RegistryMutationResult.Failure(RegistryMutationStatus.ValidationFailed, "The current clock precedes hold creation.");
        var before = AuditSnapshot(hold);
        hold.ReleasedAt = now;
        hold.ReleasedByUserId = access.UserId;
        AddAudit(hold, access.UserId, now, "retention.holdreleased", before);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RegistryMutationResult.Success(hold.Id);
    }

    private Task<bool> IsAdministratorAsync(RegistryAccessContext access, CancellationToken cancellationToken) =>
        access.Roles.Contains(ApplicationRoles.Administrator) && access.UserId != Guid.Empty
            ? database.Users.AnyAsync(user => user.Id == access.UserId && !user.IsDisabled, cancellationToken)
            : Task.FromResult(false);

    private Task<bool> ScopeExistsAsync(string scope, Guid id, CancellationToken cancellationToken) => scope switch
    {
        "Client" => database.Clients.AnyAsync(item => item.Id == id, cancellationToken),
        "Website" => database.Websites.AnyAsync(item => item.Id == id, cancellationToken),
        "Environment" => database.Environments.AnyAsync(item => item.Id == id, cancellationToken),
        "Endpoint" => database.Endpoints.AnyAsync(item => item.Id == id, cancellationToken),
        "Monitor" => database.EndpointMonitors.AnyAsync(item => item.Id == id, cancellationToken),
        "LogicalCheck" => database.LogicalChecks.AnyAsync(item => item.Id == id, cancellationToken),
        "Incident" => database.Incidents.AnyAsync(item => item.Id == id, cancellationToken),
        "CrawlRun" => database.CrawlRuns.AnyAsync(item => item.Id == id, cancellationToken),
        "PageAuditRun" => database.PageAuditRuns.AnyAsync(item => item.Id == id, cancellationToken),
        _ => Task.FromResult(false)
    };

    private void AddAudit(RetentionHold hold, Guid actor, DateTimeOffset now, string action, string? before) =>
        database.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = actor,
            ActorIdentifier = actor.ToString(),
            OccurredAt = now,
            Action = action,
            EntityType = "retention_hold",
            EntityIdentifier = hold.Id.ToString(),
            Outcome = "succeeded",
            BeforeValues = before,
            AfterValues = AuditSnapshot(hold)
        });

    private static string AuditSnapshot(RetentionHold hold) => JsonSerializer.Serialize(new
    {
        hold.Id,
        hold.ScopeType,
        hold.ScopeId,
        hold.CreatedByUserId,
        hold.CreatedAt,
        hold.ExpiresAt,
        hold.ReleasedByUserId,
        hold.ReleasedAt
    });

    private DateTimeOffset UtcNow()
    {
        var now = timeProvider.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }
}
