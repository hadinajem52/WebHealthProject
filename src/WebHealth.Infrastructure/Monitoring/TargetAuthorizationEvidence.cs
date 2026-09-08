using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Monitoring;

public sealed class TargetAuthorizationEvidence
{
    public Guid Id { get; set; }
    public Guid EndpointId { get; set; }
    public required string NormalizedHost { get; set; }
    public int Port { get; set; }
    public required string AuthorizationKind { get; set; }
    public required string EvidenceReference { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public string? RevocationReason { get; set; }
}

internal sealed class TargetAuthorizationEvidenceConfiguration : IEntityTypeConfiguration<TargetAuthorizationEvidence>
{
    public void Configure(EntityTypeBuilder<TargetAuthorizationEvidence> builder)
    {
        builder.ToTable("target_authorization_evidence", table =>
        {
            table.HasCheckConstraint("ck_target_authorization_evidence_port", "port BETWEEN 1 AND 65535");
            table.HasCheckConstraint("ck_target_authorization_evidence_kind", "authorization_kind IN ('Owned', 'ExplicitPermission')");
            table.HasCheckConstraint("ck_target_authorization_evidence_expiry", "expires_at IS NULL OR expires_at > effective_from");
            table.HasCheckConstraint("ck_target_authorization_evidence_revocation",
                "(revoked_at IS NULL AND revoked_by_user_id IS NULL AND revocation_reason IS NULL) "
                + "OR (revoked_at IS NOT NULL AND revoked_by_user_id IS NOT NULL AND revocation_reason IS NOT NULL AND length(revocation_reason) > 0)");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.NormalizedHost).HasMaxLength(253).IsRequired();
        builder.Property(item => item.AuthorizationKind).HasMaxLength(30).IsRequired();
        builder.Property(item => item.EvidenceReference).HasMaxLength(500).IsRequired();
        builder.Property(item => item.RevocationReason).HasMaxLength(500);
        builder.HasIndex(item => new { item.EndpointId, item.NormalizedHost, item.Port });
        builder.HasOne<Endpoint>().WithMany().HasForeignKey(item => item.EndpointId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TargetConnectionAuthorization(ApplicationDbContext database, TimeProvider timeProvider)
    : ITargetConnectionAuthorization
{
    public Task<bool> IsAuthorizedAsync(Guid endpointId, string host, int port, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var normalizedHost = host.TrimEnd('.').ToLowerInvariant();
        return database.TargetAuthorizationEvidence.AsNoTracking().AnyAsync(item =>
            item.EndpointId == endpointId && item.NormalizedHost == normalizedHost && item.Port == port
            && item.RevokedAt == null && item.EffectiveFrom <= now && (item.ExpiresAt == null || item.ExpiresAt > now),
            cancellationToken);
    }
}
