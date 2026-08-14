using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebHealth.Application.Assignments;
using WebHealth.Application.Auditing;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Assignments;

public sealed class TeamAdministrationService(
    ApplicationDbContext dbContext,
    IAuditTrailWriter auditTrail) : ITeamAdministrationService
{
    private const string TeamNameIndex = "ix_team_normalized_name_normalization_version";

    public async Task<IReadOnlyList<ManagedTeam>> ListTeamsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var teams = await dbContext.Teams.AsNoTracking()
            .OrderBy(team => team.Name)
            .Select(team => new
            {
                team.Id,
                team.Name,
                team.IsDisabled,
                team.Version,
                Members = team.Members
                    .Where(member => member.EffectiveFrom <= now
                        && (member.EffectiveUntil == null || member.EffectiveUntil > now))
                    .OrderBy(member => member.User.DisplayName)
                    .Select(member => new TeamMemberSummary(
                        member.UserId,
                        member.User.DisplayName,
                        member.User.Email ?? string.Empty))
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return teams.Select(team => new ManagedTeam(
            team.Id,
            team.Name,
            team.IsDisabled,
            team.Version,
            team.Members)).ToArray();
    }

    public async Task<ManagedTeam?> FindTeamAsync(
        Guid teamId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await dbContext.Teams.AsNoTracking()
            .Where(team => team.Id == teamId)
            .Select(team => new ManagedTeam(
                team.Id,
                team.Name,
                team.IsDisabled,
                team.Version,
                team.Members
                    .Where(member => member.EffectiveFrom <= now
                        && (member.EffectiveUntil == null || member.EffectiveUntil > now))
                    .OrderBy(member => member.User.DisplayName)
                    .Select(member => new TeamMemberSummary(
                        member.UserId,
                        member.User.DisplayName,
                        member.User.Email ?? string.Empty))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<TeamAdministrationResult> CreateTeamAsync(
        CreateManagedTeam command,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var name = NameNormalizer.TrimDisplayName(command.Name);
        var errors = ValidateName(name);
        if (errors.Count > 0)
        {
            return TeamAdministrationResult.Failure(errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var memberUserIds = command.MemberUserIds.Distinct().Order().ToArray();
        errors.AddRange(await ValidateNameAvailableAsync(name, null, cancellationToken));
        errors.AddRange(await LockAndValidateMembersAsync(memberUserIds, [], cancellationToken));
        if (errors.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TeamAdministrationResult.Failure(errors);
        }

        var now = DateTimeOffset.UtcNow;
        var team = CreateTeam(name, actorUserId, now);
        dbContext.Teams.Add(team);
        AddMemberships(team.Id, memberUserIds, actorUserId, now);
        dbContext.OwnerSubjects.Add(new OwnerSubject
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            CreatedAt = now,
            CreatedByUserId = actorUserId
        });

        try
        {
            await auditTrail.RecordTeamCreatedAsync(
                new AuditWriteContext(actorUserId, now),
                ToAuditSnapshot(team, memberUserIds),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TeamAdministrationResult.Success(team.Id);
        }
        catch (DbUpdateException exception) when (IsTeamNameConflict(exception))
        {
            return await RollBackNameConflictAsync(transaction, cancellationToken);
        }
    }

    public async Task<TeamAdministrationResult> UpdateTeamAsync(
        UpdateManagedTeam command,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var name = NameNormalizer.TrimDisplayName(command.Name);
        var errors = ValidateName(name);
        if (errors.Count > 0)
        {
            return TeamAdministrationResult.Failure(errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var team = await dbContext.Teams.SingleOrDefaultAsync(
            candidate => candidate.Id == command.TeamId,
            cancellationToken);
        if (team is null)
        {
            return TeamAdministrationResult.Failure("The team no longer exists.");
        }

        dbContext.Entry(team).Property(candidate => candidate.Version).OriginalValue = command.Version;
        var now = DateTimeOffset.UtcNow;
        var currentMemberships = await FindCurrentMembershipsAsync(team.Id, now, cancellationToken);
        var previousMemberIds = currentMemberships.Select(member => member.UserId).Order().ToArray();
        var requestedMemberIds = command.MemberUserIds.Distinct().Order().ToArray();
        errors.AddRange(await ValidateNameAvailableAsync(name, team.Id, cancellationToken));
        errors.AddRange(await LockAndValidateMembersAsync(
            requestedMemberIds,
            previousMemberIds,
            cancellationToken));
        if (errors.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return TeamAdministrationResult.Failure(errors);
        }

        var before = ToAuditSnapshot(team, previousMemberIds);
        CloseRemovedMemberships(currentMemberships, requestedMemberIds, now);
        AddMemberships(
            team.Id,
            requestedMemberIds.Except(previousMemberIds),
            actorUserId,
            now);
        UpdateTeam(team, command, name, actorUserId, now);

        try
        {
            await auditTrail.RecordTeamUpdatedAsync(
                new AuditWriteContext(actorUserId, now),
                before,
                ToAuditSnapshot(team, requestedMemberIds),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TeamAdministrationResult.Success(team.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return TeamAdministrationResult.Failure(
                "This team changed after you opened it. Reload the page and try again.");
        }
        catch (DbUpdateException exception) when (IsTeamNameConflict(exception))
        {
            return await RollBackNameConflictAsync(transaction, cancellationToken);
        }
    }

    private async Task<List<string>> LockAndValidateMembersAsync(
        IReadOnlyCollection<Guid> requestedUserIds,
        IReadOnlyCollection<Guid> retainedUserIds,
        CancellationToken cancellationToken)
    {
        if (requestedUserIds.Count == 0)
        {
            return [];
        }

        var users = await dbContext.Users
            .FromSqlInterpolated($"""
                SELECT *
                FROM web_health.app_user
                WHERE id = ANY ({requestedUserIds.ToArray()})
                FOR SHARE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var retainedIds = retainedUserIds.ToHashSet();
        var hasMissingUser = users.Count != requestedUserIds.Count;
        var hasNewDisabledUser = users.Any(user => user.IsDisabled && !retainedIds.Contains(user.Id));

        return hasMissingUser || hasNewDisabledUser
            ? ["One or more selected users do not exist or are disabled."]
            : [];
    }

    private async Task<List<string>> ValidateNameAvailableAsync(
        string name,
        Guid? currentTeamId,
        CancellationToken cancellationToken)
    {
        var normalizedName = NameNormalizer.Normalize(name);
        var duplicateExists = await dbContext.Teams.AnyAsync(
            team => team.Id != currentTeamId
                && team.NormalizedName == normalizedName
                && team.NormalizationVersion == NameNormalizer.Version,
            cancellationToken);
        return duplicateExists ? ["A team with this name already exists."] : [];
    }

    private Task<List<TeamMember>> FindCurrentMembershipsAsync(
        Guid teamId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        dbContext.TeamMembers
            .Where(member => member.TeamId == teamId
                && member.EffectiveFrom <= now
                && (member.EffectiveUntil == null || member.EffectiveUntil > now))
            .ToListAsync(cancellationToken);

    private static List<string> ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ["Enter a team name."];
        }

        return name.Length > 200 ? ["The team name cannot exceed 200 characters."] : [];
    }

    private static Team CreateTeam(string name, Guid actorUserId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        NormalizedName = NameNormalizer.Normalize(name),
        NormalizationVersion = NameNormalizer.Version,
        CreatedAt = now,
        CreatedByUserId = actorUserId,
        UpdatedAt = now,
        UpdatedByUserId = actorUserId,
        Version = 1
    };

    private static void UpdateTeam(
        Team team,
        UpdateManagedTeam command,
        string name,
        Guid actorUserId,
        DateTimeOffset now)
    {
        team.Name = name;
        team.NormalizedName = NameNormalizer.Normalize(name);
        team.IsDisabled = command.IsDisabled;
        team.UpdatedAt = now;
        team.UpdatedByUserId = actorUserId;
        team.Version++;
    }

    private void AddMemberships(
        Guid teamId,
        IEnumerable<Guid> userIds,
        Guid actorUserId,
        DateTimeOffset now)
    {
        foreach (var userId in userIds.Distinct())
        {
            dbContext.TeamMembers.Add(new TeamMember
            {
                Id = Guid.NewGuid(),
                TeamId = teamId,
                UserId = userId,
                EffectiveFrom = now,
                CreatedAt = now,
                CreatedByUserId = actorUserId
            });
        }
    }

    private static void CloseRemovedMemberships(
        IEnumerable<TeamMember> currentMemberships,
        IReadOnlyCollection<Guid> requestedMemberIds,
        DateTimeOffset now)
    {
        foreach (var membership in currentMemberships.Where(
                     member => !requestedMemberIds.Contains(member.UserId)))
        {
            membership.EffectiveUntil = now;
        }
    }

    private static TeamAuditSnapshot ToAuditSnapshot(
        Team team,
        IEnumerable<Guid> memberUserIds) => new(
            team.Id,
            team.Name,
            team.IsDisabled,
            memberUserIds.Order().ToArray());

    private async Task<TeamAdministrationResult> RollBackNameConflictAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return TeamAdministrationResult.Failure("A team with this name already exists.");
    }

    private static bool IsTeamNameConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: TeamNameIndex
        };
}
