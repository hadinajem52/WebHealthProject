using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Infrastructure.Incidents;

namespace WebHealth.Infrastructure.Notifications;

internal sealed class NotificationEventConfiguration : IEntityTypeConfiguration<NotificationEvent>
{
    public void Configure(EntityTypeBuilder<NotificationEvent> builder)
    {
        builder.ToTable("notification_event", table =>
        {
            table.HasCheckConstraint(
                "ck_notification_event_source_kind",
                "source_kind IN ('IncidentEvent', 'Reminder', 'Escalation')");
            table.HasCheckConstraint(
                "ck_notification_event_type",
                "event_type IN ('Opened', 'Recovered', 'Reminder', 'Escalated')");
            table.HasCheckConstraint(
                "ck_notification_event_incident_event_required",
                "(source_kind = 'IncidentEvent' AND incident_event_id IS NOT NULL) OR "
                + "(source_kind IN ('Reminder', 'Escalation') AND incident_event_id IS NULL)");
            table.HasCheckConstraint(
                "ck_notification_event_suppression_fields",
                "(is_suppressed AND suppression_reason IS NOT NULL AND length(suppression_reason) > 0) OR "
                + "(NOT is_suppressed AND suppression_reason IS NULL)");
        });
        builder.Property(notificationEvent => notificationEvent.SourceKind).HasMaxLength(20).IsRequired();
        builder.Property(notificationEvent => notificationEvent.EventType).HasMaxLength(20).IsRequired();
        builder.Property(notificationEvent => notificationEvent.OccurrenceKey).HasMaxLength(200).IsRequired();
        builder.Property(notificationEvent => notificationEvent.TemplateVersion).HasMaxLength(20).IsRequired();
        builder.Property(notificationEvent => notificationEvent.SuppressionReason).HasMaxLength(100);
        builder.HasIndex(notificationEvent => new
        {
            notificationEvent.IncidentId,
            notificationEvent.SourceKind,
            notificationEvent.EventType,
            notificationEvent.OccurrenceKey
        }).IsUnique().HasDatabaseName("ux_notification_event_occurrence");
        builder.HasOne(notificationEvent => notificationEvent.Incident).WithMany()
            .HasForeignKey(notificationEvent => notificationEvent.IncidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(notificationEvent => notificationEvent.IncidentEvent).WithMany()
            .HasForeignKey(notificationEvent => notificationEvent.IncidentEventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_delivery", table =>
        {
            table.HasCheckConstraint("ck_notification_delivery_channel", "channel IN ('Email')");
            table.HasCheckConstraint(
                "ck_notification_delivery_state",
                "state IN ('Pending', 'Processing', 'Sent', 'RetryScheduled', 'FailedPermanently', 'Suppressed')");
            table.HasCheckConstraint("ck_notification_delivery_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint(
                "ck_notification_delivery_lease_fields",
                "(lease_owner IS NULL AND lease_expires_at IS NULL) OR "
                + "(lease_owner IS NOT NULL AND lease_expires_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_notification_delivery_sent_fields",
                "(state = 'Sent' AND sent_at IS NOT NULL) OR (state <> 'Sent' AND sent_at IS NULL)");
        });
        builder.Property(delivery => delivery.Channel).HasMaxLength(20).IsRequired();
        builder.Property(delivery => delivery.NormalizedRecipient).HasMaxLength(320).IsRequired();
        builder.Property(delivery => delivery.State).HasMaxLength(20).IsRequired();
        builder.Property(delivery => delivery.LeaseOwner).HasMaxLength(100);
        builder.HasIndex(delivery => new
        {
            delivery.NotificationEventId,
            delivery.Channel,
            delivery.NormalizedRecipient
        }).IsUnique().HasDatabaseName("ux_notification_delivery_recipient");
        builder.HasIndex(delivery => new { delivery.State, delivery.NextAttemptAt });
        builder.HasOne(delivery => delivery.NotificationEvent).WithMany(notificationEvent => notificationEvent.Deliveries)
            .HasForeignKey(delivery => delivery.NotificationEventId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_notification_delivery_notification_event_id");
    }
}

internal sealed class NotificationAttemptConfiguration : IEntityTypeConfiguration<NotificationAttempt>
{
    public void Configure(EntityTypeBuilder<NotificationAttempt> builder)
    {
        builder.ToTable("notification_attempt", table =>
        {
            table.HasCheckConstraint("ck_notification_attempt_number", "attempt_number > 0");
            table.HasCheckConstraint(
                "ck_notification_attempt_outcome",
                "transport_outcome IN ('Sent', 'TransientFailure', 'PermanentFailure')");
        });
        builder.Property(attempt => attempt.TransportOutcome).HasMaxLength(30).IsRequired();
        builder.Property(attempt => attempt.SafeResponse).HasMaxLength(200);
        builder.HasIndex(attempt => new { attempt.NotificationDeliveryId, attempt.AttemptNumber })
            .IsUnique().HasDatabaseName("ux_notification_attempt_number");
        builder.HasOne(attempt => attempt.Delivery).WithMany(delivery => delivery.Attempts)
            .HasForeignKey(attempt => attempt.NotificationDeliveryId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_notification_attempt_notification_delivery_id");
    }
}
