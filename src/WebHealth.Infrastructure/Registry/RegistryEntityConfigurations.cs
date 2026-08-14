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
                "(client_id IS NOT NULL)::int + (website_id IS NOT NULL)::int + (environment_id IS NOT NULL)::int = 1");
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
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(grant => grant.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(grant => grant.RevokedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(grant => new { grant.UserId, grant.ClientId, grant.EffectiveFrom });
        builder.HasIndex(grant => new { grant.UserId, grant.WebsiteId, grant.EffectiveFrom });
        builder.HasIndex(grant => new { grant.UserId, grant.EnvironmentId, grant.EffectiveFrom });
    }
}
