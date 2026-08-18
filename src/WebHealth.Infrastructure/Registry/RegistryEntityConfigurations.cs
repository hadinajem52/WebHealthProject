using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Assignments;
using WebHealth.Infrastructure.Identity;

namespace WebHealth.Infrastructure.Registry;

internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("client");
        builder.Property(client => client.Name).HasMaxLength(200).IsRequired();
        builder.Property(client => client.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(client => client.NormalizationVersion).HasDefaultValue(NameNormalizer.Version);
        builder.Property(client => client.Notes).HasMaxLength(2000);
        builder.Property(client => client.Version).IsConcurrencyToken();
        builder.HasIndex(client => new { client.NormalizedName, client.NormalizationVersion })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(client => new { client.DeletedAt, client.IsActive, client.Name });
        builder.HasOne<OwnerSubject>().WithMany().HasForeignKey(client => client.OwnerSubjectId)
            .OnDelete(DeleteBehavior.Restrict);
        ConfigureActors(builder);
    }

    private static void ConfigureActors(EntityTypeBuilder<Client> builder)
    {
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(client => client.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(client => client.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(client => client.DeletedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WebsiteConfiguration : IEntityTypeConfiguration<Website>
{
    public void Configure(EntityTypeBuilder<Website> builder)
    {
        builder.ToTable("website");
        builder.Property(website => website.Name).HasMaxLength(200).IsRequired();
        builder.Property(website => website.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(website => website.NormalizationVersion).HasDefaultValue(NameNormalizer.Version);
        builder.Property(website => website.TechnologyCms).HasMaxLength(200);
        builder.Property(website => website.Version).IsConcurrencyToken();
        builder.HasIndex(website => new
        {
            website.ClientId,
            website.NormalizedName,
            website.NormalizationVersion
        })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(website => new { website.ClientId, website.DeletedAt, website.IsEnabled });
        builder.HasOne(website => website.Client).WithMany(client => client.Websites)
            .HasForeignKey(website => website.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OwnerSubject>().WithMany().HasForeignKey(website => website.OwnerSubjectId)
            .OnDelete(DeleteBehavior.Restrict);
        ConfigureActors(builder);
    }

    private static void ConfigureActors(EntityTypeBuilder<Website> builder)
    {
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(website => website.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(website => website.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(website => website.DeletedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tag");
        builder.Property(tag => tag.Name).HasMaxLength(100).IsRequired();
        builder.Property(tag => tag.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(tag => tag.NormalizationVersion).HasDefaultValue(NameNormalizer.Version);
        builder.Property(tag => tag.Version).IsConcurrencyToken();
        builder.HasIndex(tag => new { tag.NormalizedName, tag.NormalizationVersion }).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(tag => tag.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WebsiteTagConfiguration : IEntityTypeConfiguration<WebsiteTag>
{
    public void Configure(EntityTypeBuilder<WebsiteTag> builder)
    {
        builder.ToTable("website_tag");
        builder.HasKey(websiteTag => new { websiteTag.WebsiteId, websiteTag.TagId });
        builder.HasOne(websiteTag => websiteTag.Website).WithMany(website => website.WebsiteTags)
            .HasForeignKey(websiteTag => websiteTag.WebsiteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(websiteTag => websiteTag.Tag).WithMany(tag => tag.WebsiteTags)
            .HasForeignKey(websiteTag => websiteTag.TagId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(websiteTag => websiteTag.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(websiteTag => websiteTag.TagId);
    }
}

internal sealed class WebsiteEnvironmentConfiguration : IEntityTypeConfiguration<WebsiteEnvironment>
{
    public void Configure(EntityTypeBuilder<WebsiteEnvironment> builder)
    {
        builder.ToTable("environment", table => table.HasCheckConstraint(
            "ck_environment_type_matches_production",
            "(environment_type = 'Production') = is_production"));
        builder.Property(environment => environment.Name).HasMaxLength(100).IsRequired();
        builder.Property(environment => environment.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(environment => environment.NormalizationVersion).HasDefaultValue(NameNormalizer.Version);
        builder.Property(environment => environment.EnvironmentType).HasMaxLength(50).IsRequired();
        builder.Property(environment => environment.BaseUrl).HasMaxLength(2048);
        builder.Property(environment => environment.Version).IsConcurrencyToken();
        builder.HasIndex(environment => new
        {
            environment.WebsiteId,
            environment.NormalizedName,
            environment.NormalizationVersion
        })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(environment => new
        {
            environment.WebsiteId,
            environment.DeletedAt,
            environment.IsActive
        });
        builder.HasOne(environment => environment.Website).WithMany(website => website.Environments)
            .HasForeignKey(environment => environment.WebsiteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(environment => environment.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(environment => environment.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(environment => environment.DeletedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccessGrantConfiguration : IEntityTypeConfiguration<AccessGrant>
{
    public void Configure(EntityTypeBuilder<AccessGrant> builder)
    {
        builder.ToTable("access_grant", table =>
        {
            table.HasCheckConstraint(
                "ck_access_grant_exactly_one_scope",
                "(client_id IS NOT NULL)::int + (website_id IS NOT NULL)::int + (environment_id IS NOT NULL)::int + (endpoint_id IS NOT NULL)::int = 1");
            table.HasCheckConstraint(
                "ck_access_grant_access_level",
                "access_level IN ('Read', 'Manage')");
            table.HasCheckConstraint(
                "ck_access_grant_expiry",
                "expires_at IS NULL OR expires_at > effective_from");
        });
        builder.Property(grant => grant.AccessLevel).HasMaxLength(20).IsRequired();
        builder.Property(grant => grant.RevocationReason).HasMaxLength(500);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(grant => grant.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Client>().WithMany().HasForeignKey(grant => grant.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Website>().WithMany().HasForeignKey(grant => grant.WebsiteId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WebsiteEnvironment>().WithMany().HasForeignKey(grant => grant.EnvironmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Endpoint>().WithMany().HasForeignKey(grant => grant.EndpointId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(grant => grant.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(grant => grant.RevokedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(grant => new { grant.UserId, grant.ClientId, grant.EffectiveFrom });
        builder.HasIndex(grant => new { grant.UserId, grant.WebsiteId, grant.EffectiveFrom });
        builder.HasIndex(grant => new { grant.UserId, grant.EnvironmentId, grant.EffectiveFrom });
        builder.HasIndex(grant => new { grant.UserId, grant.EndpointId, grant.EffectiveFrom });
    }
}

internal sealed class EndpointConfiguration : IEntityTypeConfiguration<Endpoint>
{
    public void Configure(EntityTypeBuilder<Endpoint> builder)
    {
        builder.ToTable("endpoint", table =>
        {
            table.HasCheckConstraint("ck_endpoint_url_hash_length", "octet_length(normalized_url_hash) = 32");
            table.HasCheckConstraint("ck_endpoint_normalized_host", "length(normalized_host) > 0");
            table.HasCheckConstraint("ck_endpoint_effective_port", "effective_port BETWEEN 1 AND 65535");
            table.HasCheckConstraint(
                "ck_endpoint_http_exception_complete",
                "(http_exception_reason IS NULL AND http_exception_approved_by_user_id IS NULL AND http_exception_approved_at IS NULL) OR "
                + "(http_exception_reason IS NOT NULL AND http_exception_approved_by_user_id IS NOT NULL AND http_exception_approved_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_endpoint_normalized_scheme",
                "normalized_url LIKE 'http://%' OR normalized_url LIKE 'https://%'");
        });
        builder.Property(endpoint => endpoint.DisplayUrl).HasMaxLength(2048).IsRequired();
        builder.Property(endpoint => endpoint.NormalizedUrl).HasMaxLength(2048).IsRequired();
        builder.Property(endpoint => endpoint.NormalizedUrlHash).HasColumnType("bytea").IsRequired();
        builder.Property(endpoint => endpoint.NormalizedHost).HasMaxLength(253).IsRequired();
        builder.Property(endpoint => endpoint.HttpExceptionReason).HasMaxLength(500);
        builder.Property(endpoint => endpoint.Version).IsConcurrencyToken();
        builder.HasIndex(endpoint => new
        {
            endpoint.EnvironmentId,
            endpoint.NormalizedUrlHash,
            endpoint.NormalizationVersion
        }).HasDatabaseName("ux_endpoint_environment_url_hash_version_active")
            .IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasIndex(endpoint => new { endpoint.EnvironmentId, endpoint.DeletedAt, endpoint.IsEnabled });
        builder.HasOne(endpoint => endpoint.Environment).WithMany(environment => environment.Endpoints)
            .HasForeignKey(endpoint => endpoint.EnvironmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OwnerSubject>().WithMany().HasForeignKey(endpoint => endpoint.OwnerSubjectId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(endpoint => endpoint.HttpExceptionApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(endpoint => endpoint.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(endpoint => endpoint.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(endpoint => endpoint.DeletedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TargetAuthorizationEvidenceConfiguration : IEntityTypeConfiguration<TargetAuthorizationEvidence>
{
    public void Configure(EntityTypeBuilder<TargetAuthorizationEvidence> builder)
    {
        builder.ToTable("target_authorization", table =>
        {
            table.HasCheckConstraint(
                "ck_target_authorization_kind",
                "authorization_kind IN ('Owned', 'ExplicitPermission')");
            table.HasCheckConstraint("ck_target_authorization_port", "port BETWEEN 1 AND 65535");
            table.HasCheckConstraint(
                "ck_target_authorization_expiry",
                "expires_at IS NULL OR expires_at > effective_from");
        });
        // Keys are assigned in application code. Without this, the Guid key convention is
        // store-generated, so evidence added to a tracked endpoint is detected as an
        // existing row and saved as an UPDATE that matches nothing.
        builder.Property(evidence => evidence.Id).ValueGeneratedNever();
        builder.Property(evidence => evidence.AuthorizationKind).HasMaxLength(30).IsRequired();
        builder.Property(evidence => evidence.EvidenceReference).HasMaxLength(500).IsRequired();
        builder.Property(evidence => evidence.NormalizedHost).HasMaxLength(253).IsRequired();
        builder.Property(evidence => evidence.RevocationReason).HasMaxLength(500);
        builder.Property(evidence => evidence.Version).IsConcurrencyToken();
        builder.HasIndex(evidence => new { evidence.EndpointId, evidence.NormalizedHost, evidence.Port })
            .IsUnique().HasFilter("revoked_at IS NULL");
        builder.HasIndex(evidence => new { evidence.EndpointId, evidence.EffectiveFrom, evidence.ExpiresAt });
        builder.HasOne(evidence => evidence.Endpoint).WithMany(endpoint => endpoint.TargetAuthorizations)
            .HasForeignKey(evidence => evidence.EndpointId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(evidence => evidence.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(evidence => evidence.RevokedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PolicyProfileConfiguration : IEntityTypeConfiguration<PolicyProfile>
{
    public void Configure(EntityTypeBuilder<PolicyProfile> builder)
    {
        builder.ToTable("policy_profile");
        builder.Property(profile => profile.Name).HasMaxLength(200).IsRequired();
        builder.Property(profile => profile.MonitorType).HasMaxLength(50).IsRequired();
        builder.Property(profile => profile.BoundedSettings).HasColumnType("jsonb").IsRequired();
        builder.Property(profile => profile.Version).IsConcurrencyToken();
        builder.HasIndex(profile => new { profile.Name, profile.MonitorType })
            .IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasData(new PolicyProfile
        {
            Id = RegistryDefaults.HttpAvailabilityPolicyProfileId,
            Name = "Default HTTP availability",
            MonitorType = RegistryDefaults.HttpAvailabilityMonitorType,
            BoundedSettings = "{}",
            IsSystem = true,
            CreatedAt = RegistryDefaults.SeedTimestamp,
            Version = 1
        });
    }
}

internal sealed class EndpointMonitorConfiguration : IEntityTypeConfiguration<EndpointMonitor>
{
    public void Configure(EntityTypeBuilder<EndpointMonitor> builder)
    {
        builder.ToTable("endpoint_monitor", table =>
        {
            table.HasCheckConstraint("ck_endpoint_monitor_positive_interval", "interval_seconds > 0");
            table.HasCheckConstraint("ck_endpoint_monitor_positive_timeout", "timeout_seconds > 0");
            table.HasCheckConstraint("ck_endpoint_monitor_positive_confirmation", "failure_confirmation_count > 0 AND recovery_confirmation_count > 0");
            table.HasCheckConstraint(
                "ck_endpoint_monitor_threshold_order",
                "(warning_threshold_ms IS NULL OR warning_threshold_ms >= 0) "
                + "AND (critical_threshold_ms IS NULL OR critical_threshold_ms >= 0) "
                + "AND (warning_threshold_ms IS NULL OR critical_threshold_ms IS NULL "
                + "OR warning_threshold_ms < critical_threshold_ms)");
        });
        builder.Property(monitor => monitor.MonitorType).HasMaxLength(50).IsRequired();
        builder.Property(monitor => monitor.BoundedOverrides).HasColumnType("jsonb").IsRequired();
        builder.Property(monitor => monitor.ConfigurationFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(monitor => monitor.Version).IsConcurrencyToken();
        builder.Property(monitor => monitor.SchedulingEnabled).HasDefaultValue(true);
        builder.HasIndex(monitor => new { monitor.EndpointId, monitor.MonitorType })
            .IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasIndex(monitor => new { monitor.NextDueAt, monitor.Id })
            .HasFilter("deleted_at IS NULL AND is_enabled AND scheduling_enabled");
        builder.HasOne(monitor => monitor.Endpoint).WithMany(endpoint => endpoint.Monitors)
            .HasForeignKey(monitor => monitor.EndpointId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(monitor => monitor.PolicyProfile).WithMany()
            .HasForeignKey(monitor => monitor.PolicyProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(monitor => monitor.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(monitor => monitor.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(monitor => monitor.DeletedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
