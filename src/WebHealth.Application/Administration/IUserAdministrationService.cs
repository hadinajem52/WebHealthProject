namespace WebHealth.Application.Administration;

public interface IUserAdministrationService
{
    Task<IReadOnlyList<ManagedUser>> ListUsersAsync(CancellationToken cancellationToken = default);

    Task<ManagedUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserAdministrationResult> CreateUserAsync(
        CreateManagedUser command,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<UserAdministrationResult> UpdateUserAsync(
        UpdateManagedUser command,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}

public sealed record ManagedUser(
    Guid Id,
    string DisplayName,
    string Email,
    bool IsDisabled,
    IReadOnlyList<string> Roles);

public sealed record CreateManagedUser(
    string DisplayName,
    string Email,
    string Password,
    IReadOnlyCollection<string> Roles);

public sealed record UpdateManagedUser(
    Guid UserId,
    string DisplayName,
    bool IsDisabled,
    IReadOnlyCollection<string> Roles,
    string? NewPassword = null);

public sealed record UserAdministrationResult(bool Succeeded, Guid? UserId, IReadOnlyList<string> Errors)
{
    public static UserAdministrationResult Success(Guid userId) => new(true, userId, []);

    public static UserAdministrationResult Failure(params IEnumerable<string> errors) =>
        new(false, null, errors.ToArray());
}
