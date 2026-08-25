using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Domain.Seo;
using WebHealth.Infrastructure.Monitoring;

namespace WebHealth.Infrastructure.Seo;

internal sealed class SeoObservationConfiguration : IEntityTypeConfiguration<SeoObservation>
{
    public void Configure(EntityTypeBuilder<SeoObservation> builder)
    {
        builder.ToTable("seo_observation", table =>
        {
            table.HasCheckConstraint(
                "ck_seo_observation_applicability",
                "applicability IN ('Applicable', 'NotApplicable')");

            table.HasCheckConstraint(
                "ck_seo_observation_applicability_fields",
                "(applicability = 'Applicable' AND not_applicable_reason IS NULL) "
                + "OR (applicability = 'NotApplicable' AND not_applicable_reason IN "
                + "('TransportFailed', 'NonSuccessStatus', 'NonHtml', 'EmptyBody', 'ExtractionFailed') "
                + "AND title IS NULL AND meta_description IS NULL AND canonical_href IS NULL "
                + "AND canonical_absolute_url IS NULL AND robots_meta IS NULL "
                + "AND title_count = 0 AND meta_description_count = 0 AND canonical_count = 0 "
                + "AND robots_meta_count = 0 "
                + "AND title_length = 0 AND meta_description_length = 0 AND canonical_length = 0 "
                + "AND robots_meta_length = 0)");

            table.HasCheckConstraint(
                "ck_seo_observation_counts",
                "title_count >= 0 AND meta_description_count >= 0 AND canonical_count >= 0 "
                + "AND robots_meta_count >= 0");

            table.HasCheckConstraint(
                "ck_seo_observation_lengths",
                "title_length >= COALESCE(length(title), 0) "
                + "AND meta_description_length >= COALESCE(length(meta_description), 0) "
                + "AND canonical_length >= COALESCE(length(canonical_href), 0) "
                + "AND robots_meta_length >= COALESCE(length(robots_meta), 0)");
        });

        builder.HasKey(observation => observation.LogicalCheckId);
        builder.Property(observation => observation.Applicability).HasMaxLength(20).IsRequired();
        builder.Property(observation => observation.NotApplicableReason).HasMaxLength(30);
        builder.Property(observation => observation.Title).HasMaxLength(SeoValueLimits.Title);
        builder.Property(observation => observation.MetaDescription).HasMaxLength(SeoValueLimits.MetaDescription);
        builder.Property(observation => observation.CanonicalHref).HasMaxLength(SeoValueLimits.CanonicalHref);
        builder.Property(observation => observation.CanonicalAbsoluteUrl).HasMaxLength(SeoValueLimits.CanonicalHref);
        builder.Property(observation => observation.RobotsMeta).HasMaxLength(SeoValueLimits.RobotsMeta);
        builder.Property(observation => observation.PolicyExpectedHost).HasMaxLength(253);
        builder.Property(observation => observation.PolicyIndexingExpectation).HasMaxLength(20);

        builder.HasIndex(observation => new { observation.EndpointMonitorId, observation.ObservedAt })
            .IsDescending(false, true);

        builder.HasOne(observation => observation.LogicalCheck).WithOne()
            .HasForeignKey<SeoObservation>(observation =>
                new { observation.LogicalCheckId, observation.EndpointMonitorId })
            .HasPrincipalKey<LogicalCheck>(check => new { check.Id, check.EndpointMonitorId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(observation => observation.EndpointMonitor).WithMany()
            .HasForeignKey(observation => observation.EndpointMonitorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
