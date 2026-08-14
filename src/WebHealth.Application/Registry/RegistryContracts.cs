namespace WebHealth.Application.Registry;

public sealed record RegistryAccessContext(Guid UserId, IReadOnlyCollection<string> Roles);

public sealed record ClientListItem(
    Guid Id,
    string Name,
    string OwnerName,
    bool IsActive,
    bool IsDeleted,
    long Version,
    int VisibleWebsiteCount);

public sealed record ClientDetails(
    Guid Id,
    string Name,
    Guid OwnerSubjectId,
    string OwnerName,
    string? Notes,
    bool IsActive,
    bool IsDeleted,
    long Version,
    IReadOnlyList<WebsiteListItem> Websites);

public sealed record WebsiteListItem(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string Name,
    string OwnerName,
    string? TechnologyCms,
    bool IsEnabled,
    bool IsDeleted,
    long Version,
    int ActiveEnvironmentCount,
    IReadOnlyList<string> Tags);

public sealed record WebsiteDetails(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string Name,
    Guid OwnerSubjectId,
    string OwnerName,
    string? TechnologyCms,
    bool IsEnabled,
    bool IsDeleted,
    long Version,
    int ActiveEnvironmentCount,
    IReadOnlyList<string> Tags);

public sealed record RegistryTagOption(Guid Id, string Name, int WebsiteCount);

public sealed record RegistryOwnerOption(Guid OwnerSubjectId, string DisplayName, string OwnerType);

public sealed record CreateClient(string Name, Guid OwnerSubjectId, string? Notes);

public sealed record UpdateClient(
    Guid ClientId,
    string Name,
    Guid OwnerSubjectId,
    string? Notes,
    bool IsActive,
    long Version);

public sealed record CreateWebsite(
    Guid ClientId,
    string Name,
    Guid OwnerSubjectId,
    string? TechnologyCms,
    bool IsEnabled,
    IReadOnlyList<string> Tags);

public sealed record UpdateWebsite(
    Guid WebsiteId,
    string Name,
    Guid OwnerSubjectId,
    string? TechnologyCms,
    bool IsEnabled,
    long Version,
    IReadOnlyList<string> Tags);

public sealed record RegistryVersionCommand(Guid EntityId, long Version);

public enum RegistryMutationStatus
{
    Succeeded,
    Forbidden,
    NotFound,
    ValidationFailed,
    ConcurrencyConflict
}

public sealed record RegistryMutationResult(
    RegistryMutationStatus Status,
    Guid? EntityId,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Status == RegistryMutationStatus.Succeeded;

    public static RegistryMutationResult Success(Guid entityId) =>
        new(RegistryMutationStatus.Succeeded, entityId, []);

    public static RegistryMutationResult Failure(
        RegistryMutationStatus status,
        params IEnumerable<string> errors) => new(status, null, errors.ToArray());
}
