using Microsoft.EntityFrameworkCore;
using WebHealth.Application;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Auditing;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Monitoring;

internal sealed class TargetPermissionService(ApplicationDbContext database, TimeProvider timeProvider) : ITargetPermissionService
{
    public async Task<TargetPermissions?> ReadAsync(Guid endpointId, RegistryAccessContext access, CancellationToken token)
    {
        if (!RegistryVisibility.CanManage(access)) return null;
        var endpoint = await database.Endpoints.AsNoTracking().SingleOrDefaultAsync(item => item.Id == endpointId, token);
        if (endpoint is null) return null;
        var items = await database.TargetAuthorizationEvidence.AsNoTracking().Where(item => item.EndpointId == endpointId)
            .OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id)
            .Select(item => new TargetPermissionItem(item.Id, item.NormalizedHost, item.Port,
                item.AuthorizationKind, item.CreatedAt, item.ExpiresAt, item.RevokedAt)).ToArrayAsync(token);
        return new(endpointId, endpoint.DisplayUrl, items);
    }

    public async Task<RegistryMutationResult> GrantAsync(GrantTargetPermission command, RegistryAccessContext access, CancellationToken token)
    {
        if (!RegistryVisibility.CanManage(access)) return RegistryMutationResult.Failure(RegistryMutationStatus.Forbidden);
        var target = EndpointUrlNormalizer.Normalize(command.Url);
        if (!target.Succeeded) return Invalid("Url", "Enter an absolute HTTP or HTTPS target URL.");
        if (command.Kind is not ("Owned" or "ExplicitPermission")) return Invalid("Kind", "Select ownership or explicit permission.");
        if (string.IsNullOrWhiteSpace(command.EvidenceReference) || command.EvidenceReference.Length > 500)
            return Invalid("EvidenceReference", "Provide an evidence reference of up to 500 characters.");
        var now = timeProvider.GetUtcNow();
        if (command.ExpiresAt <= now) return Invalid("ExpiresAt", "Expiry must be in the future.");
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var endpoint = await database.Endpoints.FromSqlInterpolated($"""
            SELECT * FROM web_health.endpoint WHERE id = {command.EndpointId} FOR UPDATE
            """).SingleOrDefaultAsync(token);
        if (endpoint is null) return RegistryMutationResult.Failure(RegistryMutationStatus.NotFound);
        if (endpoint.DeletedAt is not null) return Invalid("EndpointId", "Restore the endpoint before granting permission.");
        if (await database.TargetAuthorizationEvidence.AnyAsync(item => item.EndpointId == endpoint.Id
            && item.NormalizedHost == target.NormalizedHost && item.Port == target.EffectivePort
            && item.RevokedAt == null && item.EffectiveFrom <= now && (item.ExpiresAt == null || item.ExpiresAt > now), token))
        {
            return Invalid("Url", "An active permission already covers this host and port. Revoke it before replacing it.");
        }
        var evidence = new TargetAuthorizationEvidence
        {
            Id = Guid.NewGuid(),
            EndpointId = endpoint.Id,
            NormalizedHost = target.NormalizedHost!,
            Port = target.EffectivePort!.Value,
            AuthorizationKind = command.Kind,
            EvidenceReference = command.EvidenceReference.Trim(),
            EffectiveFrom = now,
            ExpiresAt = command.ExpiresAt,
            CreatedAt = now,
            CreatedByUserId = access.UserId
        };
        database.TargetAuthorizationEvidence.Add(evidence);
        AddAudit(evidence.Id, access.UserId, now, "target-permission.granted");
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return RegistryMutationResult.Success(evidence.Id);
    }

    public async Task<RegistryMutationResult> RevokeAsync(Guid endpointId, Guid permissionId, string reason, RegistryAccessContext access, CancellationToken token)
    {
        if (!RegistryVisibility.CanManage(access)) return RegistryMutationResult.Failure(RegistryMutationStatus.Forbidden);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) return Invalid("Reason", "Provide a revocation reason of up to 500 characters.");
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var evidence = await database.TargetAuthorizationEvidence.FromSqlInterpolated($"""
            SELECT * FROM web_health.target_authorization_evidence
            WHERE id = {permissionId} AND endpoint_id = {endpointId} FOR UPDATE
            """).SingleOrDefaultAsync(token);
        if (evidence is null) return RegistryMutationResult.Failure(RegistryMutationStatus.NotFound);
        await database.Entry(evidence).ReloadAsync(token);
        if (evidence.RevokedAt is null)
        {
            var now = timeProvider.GetUtcNow();
            evidence.RevokedAt = now;
            evidence.RevokedByUserId = access.UserId;
            evidence.RevocationReason = reason.Trim();
            AddAudit(evidence.Id, access.UserId, now, "target-permission.revoked");
            await database.SaveChangesAsync(token);
        }
        await transaction.CommitAsync(token);
        return RegistryMutationResult.Success(permissionId);
    }

    private void AddAudit(Guid id, Guid actor, DateTimeOffset now, string action) => database.AuditEvents.Add(new AuditEvent
    {
        Id = Guid.NewGuid(),
        ActorUserId = actor,
        ActorIdentifier = actor.ToString(),
        OccurredAt = now,
        Action = action,
        EntityType = "target-permission",
        EntityIdentifier = id.ToString(),
        Outcome = "Succeeded"
    });

    private static RegistryMutationResult Invalid(string field, string message) =>
        RegistryMutationResult.Failure(RegistryMutationStatus.ValidationFailed, ValidationError.For(field, message));
}
