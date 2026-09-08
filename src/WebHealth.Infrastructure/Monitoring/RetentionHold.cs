using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WebHealth.Infrastructure.Monitoring;

public sealed class RetentionHold
{
    public Guid Id { get; set; }
    public required string ScopeType { get; set; }
    public Guid ScopeId { get; set; }
    public required string Reason { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid? ReleasedByUserId { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
}

internal sealed class RetentionHoldConfiguration : IEntityTypeConfiguration<RetentionHold>
{
    public void Configure(EntityTypeBuilder<RetentionHold> builder)
    {
        builder.HasKey(hold => hold.Id);
        builder.Property(hold => hold.ScopeType).HasMaxLength(20);
        builder.Property(hold => hold.Reason).HasMaxLength(500);
        builder.ToTable("retention_hold", table =>
        {
            table.HasCheckConstraint("ck_retention_hold_scope",
                "scope_type IN ('Client', 'Website', 'Environment', 'Endpoint', 'Monitor', 'LogicalCheck', 'Incident', 'CrawlRun', 'PageAuditRun') AND scope_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_retention_hold_reason", "length(btrim(reason)) BETWEEN 1 AND 500");
            table.HasCheckConstraint("ck_retention_hold_creator", "created_by_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_retention_hold_expiry", "expires_at IS NULL OR expires_at > created_at");
            table.HasCheckConstraint("ck_retention_hold_release",
                "(released_at IS NULL AND released_by_user_id IS NULL) OR (released_at IS NOT NULL AND released_at >= created_at AND released_by_user_id IS NOT NULL AND released_by_user_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
        });
    }
}
