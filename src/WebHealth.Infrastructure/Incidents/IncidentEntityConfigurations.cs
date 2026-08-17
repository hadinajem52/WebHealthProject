using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Infrastructure.Assignments;
using WebHealth.Infrastructure.Identity;

namespace WebHealth.Infrastructure.Incidents;

internal sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("incident", table =>
        {
            table.HasCheckConstraint("ck_incident_severity", "severity IN ('Warning', 'Critical')");
            table.HasCheckConstraint(
                "ck_incident_status",
                "status IN ('Open', 'Acknowledged', 'InProgress', 'MonitoringRecovery', 'Resolved', 'Closed')");
            table.HasCheckConstraint("ck_incident_recurrence_count", "recurrence_count >= 0");
            table.HasCheckConstraint(
                "ck_incident_acknowledged_required",
                "status NOT IN ('Acknowledged', 'InProgress', 'MonitoringRecovery', 'Resolved', 'Closed') "
                + "OR acknowledged_at IS NOT NULL");
            table.HasCheckConstraint(
                "ck_incident_resolution_complete",
                "(status NOT IN ('Resolved', 'Closed') AND resolution_category IS NULL "
                + "AND resolution_note IS NULL AND resolved_at IS NULL) OR "
                + "(status IN ('Resolved', 'Closed') AND resolution_category IS NOT NULL "
                + "AND resolution_note IS NOT NULL AND resolved_at IS NOT NULL)");
            table.HasCheckConstraint("ck_incident_closed_required", "status <> 'Closed' OR closed_at IS NOT NULL");
            table.HasCheckConstraint(
                "ck_incident_lifecycle_order",
                "(acknowledged_at IS NULL OR acknowledged_at >= opened_at) "
                + "AND (resolved_at IS NULL OR resolved_at >= opened_at) "
                + "AND (closed_at IS NULL OR (resolved_at IS NOT NULL AND closed_at >= resolved_at))");
        });
        builder.Property(incident => incident.IssueKey).HasMaxLength(200).IsRequired();
        builder.Property(incident => incident.Severity).HasMaxLength(20).IsRequired();
        builder.Property(incident => incident.Status).HasMaxLength(30).IsRequired();
        builder.Property(incident => incident.ResolutionCategory).HasMaxLength(50);
        builder.Property(incident => incident.ResolutionNote).HasMaxLength(2000);
        builder.Property(incident => incident.Version).IsConcurrencyToken();
        builder.HasIndex(incident => new { incident.EndpointMonitorId, incident.IssueKey })
            .IsUnique()
            .HasFilter("status IN ('Open', 'Acknowledged', 'InProgress', 'MonitoringRecovery')");
        builder.HasIndex(incident => new { incident.Status, incident.Severity, incident.OpenedAt });
        builder.HasIndex(incident => incident.OwnerSubjectId);
        builder.HasOne(incident => incident.EndpointMonitor).WithMany()
            .HasForeignKey(incident => incident.EndpointMonitorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(incident => incident.OwnerSubject).WithMany()
            .HasForeignKey(incident => incident.OwnerSubjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(incident => incident.PreviousIncident).WithMany()
            .HasForeignKey(incident => incident.PreviousIncidentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IncidentEventConfiguration : IEntityTypeConfiguration<IncidentEvent>
{
    public void Configure(EntityTypeBuilder<IncidentEvent> builder)
    {
        builder.ToTable("incident_event", table =>
        {
            table.HasCheckConstraint("ck_incident_event_sequence_number", "sequence_number > 0");
            table.HasCheckConstraint(
                "ck_incident_event_type",
                "event_type IN ('Opened', 'StatusChanged', 'Reassigned', 'NoteAdded')");
        });
        builder.Property(incidentEvent => incidentEvent.EventType).HasMaxLength(30).IsRequired();
        builder.Property(incidentEvent => incidentEvent.FromStatus).HasMaxLength(30);
        builder.Property(incidentEvent => incidentEvent.ToStatus).HasMaxLength(30);
        builder.Property(incidentEvent => incidentEvent.BoundedNote).HasMaxLength(2000);
        builder.HasIndex(incidentEvent => new { incidentEvent.IncidentId, incidentEvent.SequenceNumber })
            .IsUnique();
        builder.HasOne(incidentEvent => incidentEvent.Incident).WithMany(incident => incident.Events)
            .HasForeignKey(incidentEvent => incidentEvent.IncidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(incidentEvent => incidentEvent.ActorUser).WithMany()
            .HasForeignKey(incidentEvent => incidentEvent.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OwnerSubject>().WithMany()
            .HasForeignKey(incidentEvent => incidentEvent.FromOwnerSubjectId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OwnerSubject>().WithMany()
            .HasForeignKey(incidentEvent => incidentEvent.ToOwnerSubjectId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IncidentEvidenceConfiguration : IEntityTypeConfiguration<IncidentEvidence>
{
    public void Configure(EntityTypeBuilder<IncidentEvidence> builder)
    {
        builder.ToTable("incident_evidence", table => table.HasCheckConstraint(
            "ck_incident_evidence_type",
            "evidence_type IN ('Opening', 'Failure', 'Recovery')"));
        builder.Property(evidence => evidence.EvidenceType).HasMaxLength(20).IsRequired();
        builder.Property(evidence => evidence.EvidenceRole).HasMaxLength(50).IsRequired();
        builder.Property(evidence => evidence.BoundedSnapshot).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(evidence => evidence.LogicalCheckId);
        builder.HasOne(evidence => evidence.Incident).WithMany(incident => incident.Evidence)
            .HasForeignKey(evidence => evidence.IncidentId).OnDelete(DeleteBehavior.Restrict);
    }
}
