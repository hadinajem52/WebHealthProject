using WebHealth.Application.Registry;

namespace WebHealth.Infrastructure.Registry;

internal abstract record RegistryCreateCoreResult;

internal sealed record RegistryCreateCompleted(RegistryMutationResult Result) : RegistryCreateCoreResult;

internal sealed record RegistryCreateDuplicateResult(RegistryCreateDuplicate Duplicate) : RegistryCreateCoreResult;

internal abstract record RegistryCreateDuplicate;

internal sealed record ClientNameDuplicate : RegistryCreateDuplicate;

internal sealed record WebsiteNameDuplicate : RegistryCreateDuplicate;

internal sealed record EnvironmentNameDuplicate : RegistryCreateDuplicate;

internal sealed record EndpointUrlDuplicate(
    Guid EnvironmentId,
    string NormalizedUrl,
    byte[] NormalizedUrlHash) : RegistryCreateDuplicate;
