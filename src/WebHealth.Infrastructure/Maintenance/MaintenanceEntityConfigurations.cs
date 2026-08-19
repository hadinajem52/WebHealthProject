using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Domain.Maintenance;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Maintenance;

internal sealed class MaintenanceWindowConfiguration : IEntityTypeConfiguration<MaintenanceWindow>
{
    public void Configure(EntityTypeBuilder<MaintenanceWindow> builder)
    {
        builder.ToTable("maintenance_window", table =>
        {
            table.HasCheckConstraint(
                "ck_maintenance_window_suppression_policy",
                "suppression_policy IN ('SuppressAll', 'None')");
            table.HasCheckConstraint("ck_maintenance_window_reason", "length(reason) > 0");
            table.HasCheckConstraint("ck_maintenance_window_updated", "updated_at >= created_at");
            table.HasCheckConstraint(
                "ck_maintenance_window_schedule_duration",
                "schedule_duration_seconds > 0");
            table.HasCheckConstraint(
                "ck_maintenance_window_recurrence",
                "(recurrence_pattern = 'None' AND recurrence_days_of_week = 0 AND recurrence_until IS NULL) "
                + "OR (recurrence_pattern = 'Daily' AND recurrence_days_of_week = 0) "
                + "OR (recurrence_pattern = 'Weekly' AND recurrence_days_of_week BETWEEN 1 AND 127)");
            table.HasCheckConstraint(
                "ck_maintenance_window_recurrence_until",
                "recurrence_until IS NULL OR recurrence_until > schedule_starts_at");
        });
        builder.Property(window => window.Reason).HasMaxLength(500).IsRequired();
        builder.Property(window => window.TimezoneId).HasMaxLength(100).IsRequired();
        builder.Property(window => window.SuppressionPolicy).HasMaxLength(20).IsRequired();
        builder.Property(window => window.RecurrencePattern).HasMaxLength(10).IsRequired().HasDefaultValue(MaintenanceRecurrencePatterns.None);
        builder.Property(window => window.Version).IsConcurrencyToken();
        builder.HasIndex(window => window.DeletedAt);
        builder.HasIndex(window => new { window.RecurrencePattern, window.ExpandedThrough })
            .HasDatabaseName("ix_maintenance_window_recurrence_expansion");
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(window => window.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(window => window.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(window => window.DeletedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MaintenanceTargetConfiguration : IEntityTypeConfiguration<MaintenanceTarget>
{
    public void Configure(EntityTypeBuilder<MaintenanceTarget> builder)
    {
        builder.ToTable("maintenance_target", table => table.HasCheckConstraint(
            "ck_maintenance_target_exactly_one_scope",
            "(client_id IS NOT NULL)::int + (website_id IS NOT NULL)::int + (environment_id IS NOT NULL)::int "
            + "+ (endpoint_id IS NOT NULL)::int + (endpoint_monitor_id IS NOT NULL)::int = 1"));
        builder.HasIndex(target => target.MaintenanceWindowId);
        builder.HasOne(target => target.MaintenanceWindow).WithMany(window => window.Targets)
            .HasForeignKey(target => target.MaintenanceWindowId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Client>().WithMany().HasForeignKey(target => target.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Website>().WithMany().HasForeignKey(target => target.WebsiteId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WebsiteEnvironment>().WithMany().HasForeignKey(target => target.EnvironmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Endpoint>().WithMany().HasForeignKey(target => target.EndpointId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EndpointMonitor>().WithMany().HasForeignKey(target => target.EndpointMonitorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MaintenanceOccurrenceConfiguration : IEntityTypeConfiguration<MaintenanceOccurrence>
{
    public void Configure(EntityTypeBuilder<MaintenanceOccurrence> builder)
    {
        builder.ToTable("maintenance_occurrence", table => table.HasCheckConstraint(
            "ck_maintenance_occurrence_interval",
            "ends_at > starts_at"));
        // BR-M05: the expander is keyed on (window, occurrence start), so re-running it cannot
        // double-book a window. The end is a function of the start and the schedule duration and
        // is deliberately not part of the key.
        builder.HasIndex(occurrence => new { occurrence.MaintenanceWindowId, occurrence.StartsAt })
            .IsUnique().HasDatabaseName("ux_maintenance_occurrence_window_start");
        builder.HasOne(occurrence => occurrence.MaintenanceWindow).WithMany(window => window.Occurrences)
            .HasForeignKey(occurrence => occurrence.MaintenanceWindowId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_maintenance_occurrence_maintenance_window_id");
    }
}
