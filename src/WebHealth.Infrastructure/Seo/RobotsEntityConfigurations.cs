using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Infrastructure.Identity;

namespace WebHealth.Infrastructure.Seo;

internal sealed class RobotsSnapshotConfiguration : IEntityTypeConfiguration<RobotsSnapshot>
{
    public const int MaxContentLength = RobotsRefreshService.MaxRobotsBytes;

    public void Configure(EntityTypeBuilder<RobotsSnapshot> builder)
    {
        builder.ToTable("robots_snapshot", table =>
        {
            table.HasCheckConstraint(
                "ck_robots_snapshot_status",
                "status IN ('Fetched', 'NotFound', 'Unavailable')");

            table.HasCheckConstraint(
                "ck_robots_snapshot_content",
                "(status = 'Fetched') OR (content IS NULL)");

            table.HasCheckConstraint(
                "ck_robots_snapshot_exception_complete",
                "(exception_reason IS NULL AND exception_approved_by_user_id IS NULL "
                + "AND exception_approved_at IS NULL) OR "
                + "(exception_reason IS NOT NULL AND exception_approved_by_user_id IS NOT NULL "
                + "AND exception_approved_at IS NOT NULL)");

            table.HasCheckConstraint("ck_robots_snapshot_expiry", "expires_at > fetched_at");
            table.HasCheckConstraint("ck_robots_snapshot_port", "port BETWEEN 1 AND 65535");
        });

        builder.HasKey(snapshot => snapshot.Origin);
        builder.Property(snapshot => snapshot.Origin).HasMaxLength(300);
        builder.Property(snapshot => snapshot.Host).HasMaxLength(253).IsRequired();
        builder.Property(snapshot => snapshot.Status).HasMaxLength(20).IsRequired();
        builder.Property(snapshot => snapshot.Content).HasMaxLength(MaxContentLength);
        builder.Property(snapshot => snapshot.ConfiguredSitemapUrl).HasMaxLength(2048);
        builder.Property(snapshot => snapshot.CheckedSitemapUrl).HasMaxLength(2048);
        builder.Property(snapshot => snapshot.ExceptionReason).HasMaxLength(500);
        builder.Property(snapshot => snapshot.Version).IsConcurrencyToken();

        builder.HasIndex(snapshot => snapshot.ExpiresAt);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(snapshot => snapshot.ExceptionApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
