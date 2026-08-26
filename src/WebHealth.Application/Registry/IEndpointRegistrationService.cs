using WebHealth.Application.Seo;
using WebHealth.Domain.PageAudits;

namespace WebHealth.Application.Registry;

public interface IEndpointRegistrationService
{
    Task<RegistryMutationResult> RegisterAsync(
        RegisterEndpointRequest request,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default);
}

public sealed record RegisterEndpointRequest(
    EndpointRegistrationHierarchy Hierarchy,
    EndpointRegistrationSettings Endpoint);

public abstract record EndpointRegistrationHierarchy;

public sealed record ExistingEnvironment(Guid EnvironmentId) : EndpointRegistrationHierarchy;

public sealed record NewEnvironment(
    Guid WebsiteId,
    EndpointRegistrationEnvironment Environment) : EndpointRegistrationHierarchy;

public sealed record NewWebsite(
    Guid ClientId,
    EndpointRegistrationWebsite Website,
    EndpointRegistrationEnvironment Environment) : EndpointRegistrationHierarchy;

public sealed record NewClient(
    EndpointRegistrationClient Client,
    EndpointRegistrationWebsite Website,
    EndpointRegistrationEnvironment Environment) : EndpointRegistrationHierarchy;

public sealed record EndpointRegistrationClient(
    string Name,
    Guid OwnerSubjectId,
    string? Notes);

public sealed record EndpointRegistrationWebsite(
    string Name,
    Guid OwnerSubjectId,
    string? TechnologyCms,
    IReadOnlyList<string> Tags);

public sealed record EndpointRegistrationEnvironment(
    string Name,
    string EnvironmentType,
    string? BaseUrl);

public sealed record EndpointRegistrationSettings
{
    public required string Url { get; init; }
    public Guid? OwnerSubjectId { get; init; }
    public required bool IsEnabled { get; init; }
    public string? HttpExceptionReason { get; init; }
    public int? IntervalMinutesOverride { get; init; }
    public bool SchedulingEnabled { get; init; } = true;
    public int? WarningThresholdMsOverride { get; init; }
    public int? CriticalThresholdMsOverride { get; init; }
    public string? SeoExpectedCanonicalHost { get; init; }
    public string SeoIndexingExpectation { get; init; } = SeoIndexingExpectations.Default;
    public bool SeoDescriptionRequired { get; init; } = true;
    public bool PageAuditEnabled { get; init; }
    public bool PageAuditSchedulingEnabled { get; init; }
    public int PageAuditIntervalHours { get; init; } = PageAuditCadence.DefaultIntervalHours;
}

public static class EndpointRegistrationFields
{
    public const string ClientId = nameof(ClientId);
    public const string ClientName = nameof(ClientName);
    public const string ClientOwnerSubjectId = nameof(ClientOwnerSubjectId);
    public const string ClientNotes = nameof(ClientNotes);
    public const string WebsiteId = nameof(WebsiteId);
    public const string WebsiteName = nameof(WebsiteName);
    public const string WebsiteOwnerSubjectId = nameof(WebsiteOwnerSubjectId);
    public const string WebsiteTechnologyCms = nameof(WebsiteTechnologyCms);
    public const string WebsiteTags = nameof(WebsiteTags);
    public const string EnvironmentId = nameof(EnvironmentId);
    public const string EnvironmentName = nameof(EnvironmentName);
    public const string EnvironmentType = nameof(EnvironmentType);
    public const string EnvironmentBaseUrl = nameof(EnvironmentBaseUrl);
    public const string Url = nameof(Url);
    public const string OwnerSubjectId = nameof(OwnerSubjectId);
    public const string HttpExceptionReason = nameof(HttpExceptionReason);
    public const string IntervalMinutesOverride = nameof(IntervalMinutesOverride);
    public const string WarningThresholdMsOverride = nameof(WarningThresholdMsOverride);
    public const string CriticalThresholdMsOverride = nameof(CriticalThresholdMsOverride);
    public const string SeoExpectedCanonicalHost = nameof(SeoExpectedCanonicalHost);
    public const string SeoIndexingExpectation = nameof(SeoIndexingExpectation);
    public const string PageAuditIntervalHours = nameof(PageAuditIntervalHours);
}
