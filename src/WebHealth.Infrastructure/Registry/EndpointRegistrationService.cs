using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WebHealth.Application;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class EndpointRegistrationService(
    ApplicationDbContext dbContext,
    RegistryHierarchyLock hierarchyLock,
    ClientRegistryService clientService,
    WebsiteRegistryService websiteService,
    EnvironmentRegistryService environmentService,
    EndpointRegistryService endpointService) : IEndpointRegistrationService
{
    public async Task<RegistryMutationResult> RegisterAsync(
        RegisterEndpointRequest request,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return RegistryMutationResult.Failure(
                RegistryMutationStatus.Forbidden,
                "Registry management is not permitted.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var hierarchyResult = await ResolveHierarchyAsync(request, access, cancellationToken);
        if (hierarchyResult is RegistrationFailed<ResolvedHierarchy> hierarchyFailure)
        {
            return await RollBackAsync(transaction, hierarchyFailure.Failure, cancellationToken);
        }

        if (hierarchyResult is not RegistrationSucceeded<ResolvedHierarchy> hierarchySuccess)
        {
            throw new InvalidOperationException("Unknown registration result.");
        }

        var hierarchy = hierarchySuccess.Value;

        if (hierarchy.CreatedWebsiteId is { } websiteId
            && !await HasEnvironmentAsync(websiteId, cancellationToken))
        {
            return await RollBackAsync(
                transaction,
                Failure(Validation(
                    EndpointRegistrationFields.EnvironmentName,
                    "The new website needs an environment before it can be enabled.")),
                cancellationToken);
        }

        var endpoint = await endpointService.CreateEndpointCoreAsync(
            ToCreateEndpoint(hierarchy.EnvironmentId, request.Endpoint),
            access,
            cancellationToken);
        var endpointResult = FromCoreResult(endpoint, RegistrationLevel.Endpoint);
        if (endpointResult is RegistrationFailed<Guid> endpointFailure)
        {
            return await RollBackAsync(transaction, endpointFailure.Failure, cancellationToken);
        }

        if (endpointResult is not RegistrationSucceeded<Guid> endpointSuccess)
        {
            throw new InvalidOperationException("Unknown registration result.");
        }

        await transaction.CommitAsync(cancellationToken);
        return RegistryMutationResult.Success(endpointSuccess.Value);
    }

    private Task<RegistrationResult<ResolvedHierarchy>> ResolveHierarchyAsync(
        RegisterEndpointRequest request,
        RegistryAccessContext access,
        CancellationToken cancellationToken) => request.Hierarchy switch
        {
            ExistingEnvironment hierarchy => ResolveExistingEnvironmentAsync(
                hierarchy, request.Endpoint.IsEnabled, cancellationToken),
            NewEnvironment hierarchy => CreateEnvironmentAsync(
                hierarchy, request.Endpoint.IsEnabled, access, cancellationToken),
            NewWebsite hierarchy => CreateWebsiteAsync(
                hierarchy, request.Endpoint.IsEnabled, access, cancellationToken),
            NewClient hierarchy => CreateClientAsync(hierarchy, access, cancellationToken),
            _ => Task.FromResult<RegistrationResult<ResolvedHierarchy>>(
                Failed<ResolvedHierarchy>(Validation(
                    null,
                    "Select how the client, website, and environment should be resolved.")))
        };

    private async Task<RegistrationResult<ResolvedHierarchy>> ResolveExistingEnvironmentAsync(
        ExistingEnvironment hierarchy,
        bool monitoringRequested,
        CancellationToken cancellationToken)
    {
        var ancestors = await hierarchyLock.LockEnvironmentHierarchyAsync(
            hierarchy.EnvironmentId,
            cancellationToken);
        if (ancestors is null
            || ancestors.Environment.DeletedAt is not null
            || ancestors.Website.DeletedAt is not null
            || ancestors.Client.DeletedAt is not null)
        {
            return Failed<ResolvedHierarchy>(Validation(
                EndpointRegistrationFields.EnvironmentId,
                "Select an environment that is not archived."));
        }

        var ancestorFailure = ValidateMonitoringAncestors(
            ancestors.Website,
            ancestors.Client,
            monitoringRequested);
        return ancestorFailure is null
            ? Succeeded(new ResolvedHierarchy(ancestors.Environment.Id))
            : Failed<ResolvedHierarchy>(ancestorFailure);
    }

    private async Task<RegistrationResult<ResolvedHierarchy>> CreateEnvironmentAsync(
        NewEnvironment hierarchy,
        bool monitoringRequested,
        RegistryAccessContext access,
        CancellationToken cancellationToken)
    {
        var ancestors = await hierarchyLock.LockWebsiteHierarchyAsync(
            hierarchy.WebsiteId,
            cancellationToken);
        if (ancestors is null
            || ancestors.Website.DeletedAt is not null
            || ancestors.Client.DeletedAt is not null)
        {
            return Failed<ResolvedHierarchy>(Validation(
                EndpointRegistrationFields.WebsiteId,
                "Select a website that is not archived."));
        }

        var ancestorFailure = ValidateMonitoringAncestors(
            ancestors.Website,
            ancestors.Client,
            monitoringRequested);
        if (ancestorFailure is not null)
        {
            return Failed<ResolvedHierarchy>(ancestorFailure);
        }

        var environment = FromCoreResult(
            await environmentService.CreateEnvironmentCoreAsync(
                ToCreateEnvironment(hierarchy.WebsiteId, hierarchy.Environment),
                access,
                cancellationToken),
            RegistrationLevel.Environment);
        return environment switch
        {
            RegistrationSucceeded<Guid> success => Succeeded(new ResolvedHierarchy(success.Value)),
            RegistrationFailed<Guid> failure => Failed<ResolvedHierarchy>(failure.Failure),
            _ => throw new InvalidOperationException("Unknown registration result.")
        };
    }

    private async Task<RegistrationResult<ResolvedHierarchy>> CreateWebsiteAsync(
        NewWebsite hierarchy,
        bool monitoringRequested,
        RegistryAccessContext access,
        CancellationToken cancellationToken)
    {
        var client = await hierarchyLock.LockClientAsync(hierarchy.ClientId, cancellationToken);
        if (client is not { DeletedAt: null })
        {
            return Failed<ResolvedHierarchy>(Validation(
                EndpointRegistrationFields.ClientId,
                "Select a client that is not archived."));
        }

        if (monitoringRequested && !client.IsActive)
        {
            return Failed<ResolvedHierarchy>(InactiveClient(client));
        }

        var website = FromCoreResult(
            await websiteService.CreateWebsiteCoreAsync(
                ToCreateWebsite(hierarchy.ClientId, hierarchy.Website),
                access,
                monitoringRequested
                    ? WebsiteCreationPolicy.MonitoredEndpointRegistration
                    : WebsiteCreationPolicy.RegisteredOnlyEndpointRegistration,
                cancellationToken),
            RegistrationLevel.Website);
        if (website is RegistrationFailed<Guid> websiteFailure)
        {
            return Failed<ResolvedHierarchy>(websiteFailure.Failure);
        }

        if (website is not RegistrationSucceeded<Guid> websiteSuccess)
        {
            throw new InvalidOperationException("Unknown registration result.");
        }

        return await CreateEnvironmentForWebsiteAsync(
            websiteSuccess.Value,
            hierarchy.Environment,
            access,
            cancellationToken);
    }

    private async Task<RegistrationResult<ResolvedHierarchy>> CreateClientAsync(
        NewClient hierarchy,
        RegistryAccessContext access,
        CancellationToken cancellationToken)
    {
        var client = FromCoreResult(
            await clientService.CreateClientCoreAsync(
                new CreateClient(
                    hierarchy.Client.Name,
                    hierarchy.Client.OwnerSubjectId,
                    hierarchy.Client.Notes),
                access,
                cancellationToken),
            RegistrationLevel.Client);
        if (client is RegistrationFailed<Guid> clientFailure)
        {
            return Failed<ResolvedHierarchy>(clientFailure.Failure);
        }

        if (client is not RegistrationSucceeded<Guid> clientSuccess)
        {
            throw new InvalidOperationException("Unknown registration result.");
        }

        var website = FromCoreResult(
            await websiteService.CreateWebsiteCoreAsync(
                ToCreateWebsite(clientSuccess.Value, hierarchy.Website),
                access,
                WebsiteCreationPolicy.MonitoredEndpointRegistration,
                cancellationToken),
            RegistrationLevel.Website);
        if (website is RegistrationFailed<Guid> websiteFailure)
        {
            return Failed<ResolvedHierarchy>(websiteFailure.Failure);
        }

        if (website is not RegistrationSucceeded<Guid> websiteSuccess)
        {
            throw new InvalidOperationException("Unknown registration result.");
        }

        return await CreateEnvironmentForWebsiteAsync(
            websiteSuccess.Value,
            hierarchy.Environment,
            access,
            cancellationToken);
    }

    private async Task<RegistrationResult<ResolvedHierarchy>> CreateEnvironmentForWebsiteAsync(
        Guid websiteId,
        EndpointRegistrationEnvironment input,
        RegistryAccessContext access,
        CancellationToken cancellationToken)
    {
        var environment = FromCoreResult(
            await environmentService.CreateEnvironmentCoreAsync(
                ToCreateEnvironment(websiteId, input),
                access,
                cancellationToken),
            RegistrationLevel.Environment);
        return environment switch
        {
            RegistrationSucceeded<Guid> success =>
                Succeeded(new ResolvedHierarchy(success.Value, websiteId)),
            RegistrationFailed<Guid> failure => Failed<ResolvedHierarchy>(failure.Failure),
            _ => throw new InvalidOperationException("Unknown registration result.")
        };
    }

    private async Task<RegistryMutationResult> RollBackAsync(
        IDbContextTransaction transaction,
        RegistrationFailure failure,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        var result = failure.Result switch
        {
            RegistryCreateDuplicateResult { Duplicate: ClientNameDuplicate } =>
                clientService.ResolveClientNameDuplicate(),
            RegistryCreateDuplicateResult { Duplicate: WebsiteNameDuplicate } =>
                websiteService.ResolveWebsiteNameDuplicate(),
            RegistryCreateDuplicateResult { Duplicate: EnvironmentNameDuplicate } =>
                environmentService.ResolveEnvironmentNameDuplicate(),
            RegistryCreateDuplicateResult { Duplicate: EndpointUrlDuplicate duplicate } =>
                await endpointService.ResolveEndpointUrlDuplicateAsync(duplicate, cancellationToken),
            RegistryCreateCompleted completed => completed.Result,
            _ => throw new InvalidOperationException("Unknown registration failure.")
        };
        return MapFields(result, failure.Level);
    }

    private static RegistryMutationResult MapFields(
        RegistryMutationResult result,
        RegistrationLevel level) => result with
        {
            Errors = result.Errors.Select(error => error with
            {
                Field = MapField(error.Field, level)
            }).ToArray()
        };

    private static string? MapField(string? field, RegistrationLevel level) => level switch
    {
        RegistrationLevel.Client => field switch
        {
            nameof(UpdateClient.Name) => EndpointRegistrationFields.ClientName,
            nameof(UpdateClient.OwnerSubjectId) => EndpointRegistrationFields.ClientOwnerSubjectId,
            nameof(UpdateClient.Notes) => EndpointRegistrationFields.ClientNotes,
            _ => field
        },
        RegistrationLevel.Website => field switch
        {
            nameof(CreateWebsite.ClientId) => EndpointRegistrationFields.ClientId,
            nameof(UpdateWebsite.Name) => EndpointRegistrationFields.WebsiteName,
            nameof(UpdateWebsite.OwnerSubjectId) => EndpointRegistrationFields.WebsiteOwnerSubjectId,
            nameof(UpdateWebsite.TechnologyCms) => EndpointRegistrationFields.WebsiteTechnologyCms,
            "Tags" => EndpointRegistrationFields.WebsiteTags,
            _ => field
        },
        RegistrationLevel.Environment => field switch
        {
            nameof(CreateEnvironment.WebsiteId) => EndpointRegistrationFields.WebsiteId,
            nameof(UpdateEnvironment.Name) => EndpointRegistrationFields.EnvironmentName,
            nameof(UpdateEnvironment.EnvironmentType) => EndpointRegistrationFields.EnvironmentType,
            nameof(UpdateEnvironment.BaseUrl) => EndpointRegistrationFields.EnvironmentBaseUrl,
            _ => field
        },
        RegistrationLevel.Endpoint => field switch
        {
            nameof(CreateEndpoint.EnvironmentId) => EndpointRegistrationFields.EnvironmentId,
            nameof(CreateEndpoint.Url) => EndpointRegistrationFields.Url,
            nameof(UpdateEndpoint.OwnerSubjectId) => EndpointRegistrationFields.OwnerSubjectId,
            nameof(UpdateEndpoint.HttpExceptionReason) => EndpointRegistrationFields.HttpExceptionReason,
            nameof(UpdateEndpoint.IntervalMinutesOverride) => EndpointRegistrationFields.IntervalMinutesOverride,
            nameof(UpdateEndpoint.WarningThresholdMsOverride) => EndpointRegistrationFields.WarningThresholdMsOverride,
            nameof(UpdateEndpoint.CriticalThresholdMsOverride) => EndpointRegistrationFields.CriticalThresholdMsOverride,
            nameof(UpdateEndpoint.SeoExpectedCanonicalHost) => EndpointRegistrationFields.SeoExpectedCanonicalHost,
            nameof(UpdateEndpoint.SeoIndexingExpectation) => EndpointRegistrationFields.SeoIndexingExpectation,
            nameof(UpdateEndpoint.PageAuditIntervalHours) => EndpointRegistrationFields.PageAuditIntervalHours,
            _ => field
        },
        _ => field
    };

    private static RegistrationFailure? ValidateMonitoringAncestors(
        Website website,
        Client client,
        bool monitoringRequested)
    {
        if (!monitoringRequested)
        {
            return null;
        }

        if (!website.IsEnabled)
        {
            return Failure(Validation(
                EndpointRegistrationFields.WebsiteId,
                $"The website '{website.Name}' is disabled. Enable it before starting monitoring, or clear Start monitoring immediately."));
        }

        return client.IsActive ? null : InactiveClient(client);
    }

    private static RegistrationFailure InactiveClient(Client client) =>
        Failure(Validation(
            EndpointRegistrationFields.ClientId,
            $"The client '{client.Name}' is inactive. Activate it before starting monitoring, or clear Start monitoring immediately."));

    private Task<bool> HasEnvironmentAsync(Guid websiteId, CancellationToken cancellationToken) =>
        dbContext.Environments.AnyAsync(environment =>
            environment.WebsiteId == websiteId
            && environment.DeletedAt == null,
            cancellationToken);

    private static CreateWebsite ToCreateWebsite(
        Guid clientId,
        EndpointRegistrationWebsite website) => new(
        clientId,
        website.Name,
        website.OwnerSubjectId,
        website.TechnologyCms,
        IsEnabled: true,
        website.Tags);

    private static CreateEnvironment ToCreateEnvironment(
        Guid websiteId,
        EndpointRegistrationEnvironment environment) => new(
        websiteId,
        environment.Name,
        environment.EnvironmentType,
        environment.BaseUrl,
        IsActive: true);

    private static CreateEndpoint ToCreateEndpoint(
        Guid environmentId,
        EndpointRegistrationSettings endpoint) => new(
        EnvironmentId: environmentId,
        Url: endpoint.Url,
        OwnerSubjectId: endpoint.OwnerSubjectId,
        IsEnabled: endpoint.IsEnabled,
        HttpExceptionReason: endpoint.HttpExceptionReason,
        IntervalMinutesOverride: endpoint.IntervalMinutesOverride,
        SchedulingEnabled: endpoint.SchedulingEnabled,
        WarningThresholdMsOverride: endpoint.WarningThresholdMsOverride,
        CriticalThresholdMsOverride: endpoint.CriticalThresholdMsOverride,
        SeoExpectedCanonicalHost: endpoint.SeoExpectedCanonicalHost,
        SeoIndexingExpectation: endpoint.SeoIndexingExpectation,
        SeoDescriptionRequired: endpoint.SeoDescriptionRequired,
        PageAuditEnabled: endpoint.PageAuditEnabled,
        PageAuditSchedulingEnabled: endpoint.PageAuditSchedulingEnabled,
        PageAuditIntervalHours: endpoint.PageAuditIntervalHours);

    private static RegistryMutationResult Validation(string? field, string message) =>
        RegistryMutationResult.Failure(
            RegistryMutationStatus.ValidationFailed,
            field is null ? new ValidationError(null, message) : ValidationError.For(field, message));

    private enum RegistrationLevel
    {
        None,
        Client,
        Website,
        Environment,
        Endpoint
    }

    private static RegistrationSucceeded<T> Succeeded<T>(T value) => new(value);

    private static RegistrationFailed<T> Failed<T>(RegistryMutationResult result) =>
        new(Failure(result));

    private static RegistrationFailed<T> Failed<T>(RegistrationFailure failure) => new(failure);

    private static RegistrationFailure Failure(
        RegistryMutationResult result,
        RegistrationLevel level = RegistrationLevel.None) =>
        new(level, new RegistryCreateCompleted(result));

    private static RegistrationResult<Guid> FromCoreResult(
        RegistryCreateCoreResult result,
        RegistrationLevel level) => result switch
        {
            RegistryCreateCompleted { Result.Succeeded: true, Result.EntityId: Guid entityId } =>
                Succeeded(entityId),
            RegistryCreateCompleted { Result.Succeeded: true } =>
                throw new InvalidOperationException("A successful registry create result needs an entity id."),
            _ => Failed<Guid>(new RegistrationFailure(level, result))
        };

    private abstract record RegistrationResult<T>;

    private sealed record RegistrationSucceeded<T>(T Value) : RegistrationResult<T>;

    private sealed record RegistrationFailed<T>(RegistrationFailure Failure) : RegistrationResult<T>;

    private sealed record RegistrationFailure(
        RegistrationLevel Level,
        RegistryCreateCoreResult Result);

    private sealed record ResolvedHierarchy(Guid EnvironmentId, Guid? CreatedWebsiteId = null);
}
