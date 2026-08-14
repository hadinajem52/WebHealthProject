using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Infrastructure.Identity;

namespace WebHealth.Infrastructure.Auditing;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_event");
        builder.Property(auditEvent => auditEvent.ActorIdentifier).HasMaxLength(36).IsRequired();
        builder.Property(auditEvent => auditEvent.Action).HasMaxLength(100).IsRequired();
        builder.Property(auditEvent => auditEvent.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(auditEvent => auditEvent.EntityIdentifier).HasMaxLength(2048).IsRequired();
        builder.Property(auditEvent => auditEvent.Outcome).HasMaxLength(50).IsRequired();
        builder.Property(auditEvent => auditEvent.RequestMethod).HasMaxLength(16).IsRequired();
        builder.Property(auditEvent => auditEvent.CorrelationId).HasMaxLength(128).IsRequired();
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(auditEvent => auditEvent.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(auditEvent => auditEvent.OccurredAt);
        builder.HasIndex(auditEvent => new { auditEvent.ActorUserId, auditEvent.OccurredAt });
        builder.HasIndex(auditEvent => new { auditEvent.Action, auditEvent.OccurredAt });
    }
}
