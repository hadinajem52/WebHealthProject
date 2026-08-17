using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Infrastructure.Monitoring;

namespace WebHealth.Infrastructure.Health;

internal sealed class IssueStateConfiguration : IEntityTypeConfiguration<IssueState>
{
    public void Configure(EntityTypeBuilder<IssueState> builder)
    {
        builder.ToTable("issue_state", table => table.HasCheckConstraint(
            "ck_issue_state_counters",
            "consecutive_failures >= 0 AND consecutive_recoveries >= 0"));
        builder.Property(state => state.IssueKey).HasMaxLength(200).IsRequired();
        builder.Property(state => state.Version).IsConcurrencyToken();
        builder.HasIndex(state => new { state.EndpointMonitorId, state.IssueKey }).IsUnique();
        builder.HasOne(state => state.EndpointMonitor).WithMany()
            .HasForeignKey(state => state.EndpointMonitorId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EndpointHealthConfiguration : IEntityTypeConfiguration<EndpointHealth>
{
    public void Configure(EntityTypeBuilder<EndpointHealth> builder)
    {
        builder.ToTable("endpoint_health", table => table.HasCheckConstraint(
            "ck_endpoint_health_status",
            "confirmed_status IN ('Unknown', 'Healthy', 'Warning', 'Critical', 'Disabled')"));
        builder.HasKey(health => health.EndpointMonitorId);
        builder.Property(health => health.ConfirmedStatus).HasMaxLength(20).IsRequired();
        builder.Property(health => health.Version).IsConcurrencyToken();
        builder.HasOne(health => health.EndpointMonitor).WithOne(monitor => monitor.EndpointHealth)
            .HasForeignKey<EndpointHealth>(health => health.EndpointMonitorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(health => new { health.EvidenceLogicalCheckId, health.EndpointMonitorId })
            .HasDatabaseName("ix_endpoint_health_evidence_check_monitor");
        builder.HasOne(health => health.EvidenceLogicalCheck).WithMany()
            .HasForeignKey(health => new { health.EvidenceLogicalCheckId, health.EndpointMonitorId })
            .HasPrincipalKey(check => new { check.Id, check.EndpointMonitorId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_endpoint_health_logical_check_monitor");
    }
}
