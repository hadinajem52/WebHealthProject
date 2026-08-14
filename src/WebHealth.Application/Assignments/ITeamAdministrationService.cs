namespace WebHealth.Application.Assignments;

public interface ITeamAdministrationService
{
    Task<IReadOnlyList<ManagedTeam>> ListTeamsAsync(CancellationToken cancellationToken = default);

    Task<ManagedTeam?> FindTeamAsync(Guid teamId, CancellationToken cancellationToken = default);

    Task<TeamAdministrationResult> CreateTeamAsync(
        CreateManagedTeam command,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<TeamAdministrationResult> UpdateTeamAsync(
        UpdateManagedTeam command,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}

public sealed record ManagedTeam(
    Guid Id,
    string Name,
    bool IsDisabled,
    long Version,
    IReadOnlyList<TeamMemberSummary> Members);

public sealed record TeamMemberSummary(Guid UserId, string DisplayName, string Email);

public sealed record CreateManagedTeam(string Name, IReadOnlyCollection<Guid> MemberUserIds);

public sealed record UpdateManagedTeam(
    Guid TeamId,
    string Name,
    bool IsDisabled,
    long Version,
    IReadOnlyCollection<Guid> MemberUserIds);

public sealed record TeamAdministrationResult(bool Succeeded, Guid? TeamId, IReadOnlyList<string> Errors)
{
    public static TeamAdministrationResult Success(Guid teamId) => new(true, teamId, []);

    public static TeamAdministrationResult Failure(params IEnumerable<string> errors) =>
        new(false, null, errors.ToArray());
}
