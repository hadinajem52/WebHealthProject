using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Identity;

namespace WebHealth.Infrastructure.Assignments;

internal sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("team");
        builder.Property(team => team.Name).HasMaxLength(200).IsRequired();
        builder.Property(team => team.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(team => team.NormalizationVersion).HasDefaultValue(NameNormalizer.Version);
        builder.Property(team => team.Version).IsConcurrencyToken();
        builder.HasIndex(team => new { team.NormalizedName, team.NormalizationVersion }).IsUnique();
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(team => team.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(team => team.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("team_member", table => table.HasCheckConstraint(
            "ck_team_member_effective_range",
            "effective_until IS NULL OR effective_until > effective_from"));
        builder.HasOne(member => member.Team)
            .WithMany(team => team.Members)
            .HasForeignKey(member => member.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(member => member.User)
            .WithMany()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(member => member.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(member => new { member.TeamId, member.UserId, member.EffectiveFrom });
        builder.HasIndex(member => new { member.UserId, member.EffectiveUntil });
    }
}

internal sealed class OwnerSubjectConfiguration : IEntityTypeConfiguration<OwnerSubject>
{
    public void Configure(EntityTypeBuilder<OwnerSubject> builder)
    {
        builder.ToTable("owner_subject", table => table.HasCheckConstraint(
            "ck_owner_subject_exactly_one_subject",
            "(user_id IS NOT NULL)::int + (team_id IS NOT NULL)::int = 1"));
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(subject => subject.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(subject => subject.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(subject => subject.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(subject => subject.UserId)
            .IsUnique()
            .HasFilter("user_id IS NOT NULL");
        builder.HasIndex(subject => subject.TeamId)
            .IsUnique()
            .HasFilter("team_id IS NOT NULL");
    }
}
