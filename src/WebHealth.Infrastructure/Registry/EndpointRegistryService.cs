using WebHealth.Application.Monitoring;
using WebHealth.Infrastructure.Monitoring;
using WebHealth.Application;
using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Auditing;
using WebHealth.Application.Registry;
using WebHealth.Application.Seo;
using WebHealth.Domain.Normalization;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;

using WebHealth.Infrastructure.PageAudits;

namespace WebHealth.Infrastructure.Registry;

internal sealed class EndpointRegistryService(
    ApplicationDbContext dbContext,
    RegistryMutationSupport mutationSupport,
    RegistryHierarchyLock hierarchyLock,
    EndpointPurgeCascade purgeCascade,
    IAuditTrailWriter auditTrail) : IEndpointRegistryService
{
    private const string EndpointUrlIndex = "ux_endpoint_environment_url_hash_version_active";

    public async Task<RegistryMutationResult> CreateAsync(
        CreateEndpoint command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var preparation = PrepareCreate(command, access);
        if (preparation.Failure is not null)
        {
            return preparation.Failure;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var coreResult = await CreateEndpointCoreAsync(command, access, cancellationToken);
        if (coreResult is RegistryCreateDuplicateResult { Duplicate: EndpointUrlDuplicate duplicate })
        {
            return await RollBackDuplicateAsync(
                transaction,
                duplicate.EnvironmentId,
                duplicate.NormalizedUrl,
                duplicate.NormalizedUrlHash,
                cancellationToken);
        }

        var result = ((RegistryCreateCompleted)coreResult).Result;
        if (result.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    internal async Task<RegistryCreateCoreResult> CreateEndpointCoreAsync(
        CreateEndpoint command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var preparation = PrepareCreate(command, access);
        if (preparation.Failure is not null)
        {
            return new RegistryCreateCompleted(preparation.Failure);
        }

        var url = preparation.Url!;
        var interval = preparation.Interval!;
        var thresholds = preparation.Thresholds!;
        var environment = await LockEnvironmentAsync(command.EnvironmentId, cancellationToken);
        if (environment is null)
        {
            return new RegistryCreateCompleted(Validation(ValidationError.For(
                nameof(CreateEndpoint.EnvironmentId),
                "Select an environment whose website is not archived. An archived environment "
                + "cannot take new endpoints.")));
        }

        if (!await IsValidOwnerAsync(command.OwnerSubjectId, null, cancellationToken))
        {
            return new RegistryCreateCompleted(Validation(ValidationError.For(
                nameof(UpdateEndpoint.OwnerSubjectId),
                "Select an enabled user or team as the owner, or leave it blank to inherit the "
                + "website owner. A disabled owner cannot own records.")));
        }

        var exception = DecideHttpException(url.NormalizedUrl!, command.HttpExceptionReason, environment.IsProduction, access, null);
        if (exception.Error is not null)
        {
            return new RegistryCreateCompleted(Validation(ValidationError.For(
                nameof(UpdateEndpoint.HttpExceptionReason), exception.Error)));
        }

        var httpOverrides = BuildHttpOverrides(command.HttpPolicy, interval.Seconds,
            command.WarningThresholdMsOverride, command.CriticalThresholdMsOverride);
        var httpPolicy = HttpMonitorConfiguration.Resolve(httpOverrides, environment.IsProduction);
        if (!httpPolicy.Succeeded) return new RegistryCreateCompleted(Validation(HttpPolicyErrors(httpPolicy.Errors)));
        var now = DateTimeOffset.UtcNow;
        var endpoint = CreateEndpointEntity(command, access.UserId, url, exception, now);
        dbContext.Endpoints.Add(endpoint);
        var availability = EndpointMonitorReconciler.CreateMonitor(
            endpoint, environment.IsProduction, interval.Seconds, command.SchedulingEnabled,
            thresholds.Thresholds, access.UserId, now, httpOverrides);
        dbContext.EndpointMonitors.Add(availability);
        if (RegistryDefaults.RequiresSslMonitor(endpoint.NormalizedUrl))
        {
            dbContext.EndpointMonitors.Add(EndpointMonitorReconciler.CreateSslMonitor(
                endpoint, environment.IsProduction, command.SchedulingEnabled, access.UserId, now));
        }
        await PageAuditConfiguration.ApplyAsync(
            dbContext, endpoint.Id, command.PageAuditEnabled, command.PageAuditSchedulingEnabled,
            command.PageAuditIntervalHours, now, cancellationToken);
        ApplyPageAuditMonitor(endpoint, environment.IsProduction, command.PageAuditEnabled, access.UserId, now);
        var pageAudit = new PageAuditConfigurationState(
            command.PageAuditEnabled, command.PageAuditSchedulingEnabled, command.PageAuditIntervalHours);

        try
        {
            await auditTrail.RecordEndpointMutationAsync(
                new(access.UserId, now), EndpointAuditAction.Created, null,
                ToAudit(endpoint, urlChanged: true, httpExceptionChanged: exception.Reason is not null, pageAudit),
                cancellationToken);
            return new RegistryCreateCompleted(RegistryMutationResult.Success(endpoint.Id));
        }
        catch (DbUpdateException exceptionError) when (IsDuplicate(exceptionError))
        {
            return new RegistryCreateDuplicateResult(new EndpointUrlDuplicate(
                command.EnvironmentId,
                url.NormalizedUrl!,
                url.NormalizedUrlHash!));
        }
    }

    public async Task<RegistryMutationResult> UpdateAsync(
        UpdateEndpoint command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        var url = EndpointUrlNormalizer.Normalize(command.Url);
        if (!url.Succeeded)
        {
            return Validation(url.Errors.Select(error =>
                ValidationError.For(nameof(CreateEndpoint.Url), error)));
        }

        if (DestinationHostPolicy.IsDefinitelyUnreachable(url.NormalizedHost, out var unreachable))
        {
            return Validation(ValidationError.For(nameof(CreateEndpoint.Url), unreachable!));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var endpoint = await dbContext.Endpoints.Include(candidate => candidate.Environment)
            .ThenInclude(environment => environment.Website)
            .Include(candidate => candidate.Monitors)
            .AsSingleQuery()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.EndpointId, cancellationToken);
        if (endpoint is null)
        {
            return NotFound();
        }

        if (endpoint.DeletedAt is not null)
        {
            return Validation("This endpoint is archived, so it cannot be edited. Restore it first, then reopen this form.");
        }

        var monitor = AvailabilityMonitor(endpoint);
        var currentIntervalOverride = MonitorIntervalOverride.GetSeconds(monitor.BoundedOverrides);
        var interval = DecideIntervalOverride(
            command.IntervalMinutesOverride, access, currentIntervalOverride);
        if (interval.Error is not null)
        {
            return Validation(ValidationError.For(nameof(UpdateEndpoint.IntervalMinutesOverride), interval.Error));
        }

        var thresholds = ResponseThresholdOverride.Decide(
            command.WarningThresholdMsOverride, command.CriticalThresholdMsOverride);
        if (thresholds.Error is not null)
        {
            return Validation(ValidationError.For(nameof(UpdateEndpoint.WarningThresholdMsOverride), thresholds.Error));
        }

        if (ValidateSeoPolicy(command.SeoIndexingExpectation, command.SeoExpectedCanonicalHost) is { } seoError)
        {
            return Validation(ValidationError.For(nameof(UpdateEndpoint.SeoIndexingExpectation), seoError));
        }

        if (PageAuditConfiguration.Validate(
            command.PageAuditEnabled,
            command.PageAuditSchedulingEnabled,
            command.PageAuditIntervalHours,
            url.NormalizedUrl!) is { } pageAuditError)
        {
            return Validation(ValidationError.For(nameof(UpdateEndpoint.PageAuditIntervalHours), pageAuditError));
        }

        if (!await IsValidOwnerAsync(command.OwnerSubjectId, endpoint.OwnerSubjectId, cancellationToken))
        {
            return Validation(ValidationError.For(
                nameof(UpdateEndpoint.OwnerSubjectId),
                "Select an enabled user or team as the owner, or leave it blank to inherit the "
                + "website owner. A disabled owner cannot own records."));
        }

        var exception = DecideHttpException(url.NormalizedUrl!, command.HttpExceptionReason,
            endpoint.Environment.IsProduction, access, endpoint);
        if (exception.Error is not null)
        {
            return Validation(ValidationError.For(nameof(UpdateEndpoint.HttpExceptionReason), exception.Error));
        }

        var httpOverrides = BuildHttpOverrides(command.HttpPolicy ?? HttpMonitorConfiguration.ReadOverrides(monitor),
            interval.Seconds, command.WarningThresholdMsOverride, command.CriticalThresholdMsOverride);
        var httpPolicy = HttpMonitorConfiguration.Resolve(httpOverrides, endpoint.Environment.IsProduction);
        if (!httpPolicy.Succeeded) return Validation(HttpPolicyErrors(httpPolicy.Errors));

        dbContext.Entry(endpoint).Property(candidate => candidate.Version).OriginalValue = command.Version;
        var urlChanged = !string.Equals(endpoint.NormalizedUrl, url.NormalizedUrl, StringComparison.Ordinal);
        var exceptionChanged = !string.Equals(endpoint.HttpExceptionReason, exception.Reason, StringComparison.Ordinal)
            || endpoint.HttpExceptionApprovedByUserId != exception.ApprovedByUserId;
        var now = DateTimeOffset.UtcNow;
        var pageAuditBefore = await PageAuditConfiguration.ReadAsync(
            dbContext, endpoint.Id, cancellationToken);
        var before = ToAudit(endpoint, urlChanged: false, httpExceptionChanged: false, pageAuditBefore);
        try
        {
            await PageAuditConfiguration.ApplyAsync(
                dbContext, endpoint.Id, command.PageAuditEnabled, command.PageAuditSchedulingEnabled,
                command.PageAuditIntervalHours, now, cancellationToken);
            ApplyEndpointUpdate(
                endpoint,
                command,
                url,
                exception,
                endpoint.Environment.IsProduction,
                interval.Seconds,
                thresholds.Thresholds,
                access.UserId,
                now);
            HttpMonitorConfiguration.Apply(monitor, endpoint.NormalizedUrl, endpoint.Environment.IsProduction, httpOverrides);
            ApplyPageAuditMonitor(
                endpoint,
                endpoint.Environment.IsProduction,
                command.PageAuditEnabled,
                access.UserId,
                now);
            await auditTrail.RecordEndpointMutationAsync(
                new(access.UserId, now), EndpointAuditAction.Updated, before,
                ToAudit(endpoint, urlChanged, exceptionChanged,
                    new(command.PageAuditEnabled, command.PageAuditSchedulingEnabled,
                        command.PageAuditIntervalHours)),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(endpoint.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RollBackConcurrencyAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException exceptionError) when (IsDuplicate(exceptionError))
        {
            return await RollBackDuplicateAsync(
                transaction, endpoint.EnvironmentId, url.NormalizedUrl!, url.NormalizedUrlHash!, cancellationToken);
        }
    }

    public Task<RegistryMutationResult> DisableAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, EndpointAuditAction.Disabled, cancellationToken);

    public Task<RegistryMutationResult> DeleteAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, EndpointAuditAction.Deleted, cancellationToken);

    public Task<RegistryMutationResult> RestoreAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(command, access, EndpointAuditAction.Restored, cancellationToken);

    public async Task<RegistryMutationResult> PurgeAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!access.Roles.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal))
        {
            return Forbidden();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var endpoint = await dbContext.Endpoints.FromSqlInterpolated($"""
            SELECT * FROM web_health.endpoint WHERE id = {command.EntityId} FOR UPDATE
            """)
            .Include(candidate => candidate.Monitors)
            .AsNoTracking()
            .AsSingleQuery()
            .SingleOrDefaultAsync(cancellationToken);
        if (endpoint is null)
        {
            return NotFound();
        }

        if (endpoint.Version != command.Version)
        {
            return await RollBackConcurrencyAsync(transaction, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;

        var pageAuditState = await PageAuditConfiguration.ReadAsync(
            dbContext, endpoint.Id, cancellationToken);
        var snapshot = ToAudit(endpoint, false, false, pageAuditState);
        await auditTrail.RecordEndpointMutationAsync(
            new(access.UserId, now), EndpointAuditAction.Purged, snapshot, snapshot, cancellationToken);
        await purgeCascade.ExecuteAsync(endpoint.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RegistryMutationResult.Success(endpoint.Id);
    }

    public Task<RegistryMutationResult> PauseScheduleAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        ChangeScheduleAsync(command, access, scheduleEnabled: false, cancellationToken);

    public Task<RegistryMutationResult> ResumeScheduleAsync(RegistryVersionCommand command, RegistryAccessContext access, CancellationToken cancellationToken = default) =>
        ChangeScheduleAsync(command, access, scheduleEnabled: true, cancellationToken);

    private async Task<RegistryMutationResult> ChangeScheduleAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        bool scheduleEnabled,
        CancellationToken cancellationToken)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var endpoint = await dbContext.Endpoints.Include(candidate => candidate.Monitors)
            .SingleOrDefaultAsync(candidate => candidate.Id == command.EntityId, cancellationToken);
        if (endpoint is null)
        {
            return NotFound();
        }

        if (endpoint.DeletedAt is not null)
        {
            return Validation("This endpoint is archived, so its schedule cannot change. Restore it first.");
        }

        var monitor = endpoint.Monitors.SingleOrDefault(candidate =>
            candidate.DeletedAt == null
            && candidate.MonitorType == RegistryDefaults.HttpAvailabilityMonitorType);
        if (monitor is null)
        {
            return Validation("This endpoint has no active monitor, so there is no schedule to change. "
                + "Open Edit endpoint and enable a check first.");
        }

        if (!monitor.SchedulingEnabled)
        {
            return Validation("This endpoint runs on-demand checks only. Turn on “Run scheduled checks” "
                + "in Edit endpoint before pausing or resuming a schedule.");
        }

        if (monitor.IsEnabled == scheduleEnabled)
        {
            return Validation(scheduleEnabled
                ? "Scheduled checks are already running."
                : "Scheduled checks are already paused.");
        }

        dbContext.Entry(endpoint).Property(candidate => candidate.Version).OriginalValue = command.Version;
        var now = DateTimeOffset.UtcNow;
        var action = scheduleEnabled
            ? EndpointAuditAction.ScheduleResumed
            : EndpointAuditAction.SchedulePaused;
        var pageAuditState = await PageAuditConfiguration.ReadAsync(
            dbContext, endpoint.Id, cancellationToken);
        var before = ToAudit(endpoint, false, false, pageAuditState);

        foreach (var active in endpoint.Monitors.Where(candidate => candidate.DeletedAt == null))
        {
            active.IsEnabled = scheduleEnabled;
            if (scheduleEnabled)
            {
                active.NextDueAt = MonitorCadence.GetResumeDueAt(active.NextDueAt, now);
            }

            active.UpdatedAt = now;
            active.UpdatedByUserId = access.UserId;
            active.Version++;
        }

        Touch(endpoint, access.UserId, now);

        try
        {
            await auditTrail.RecordEndpointMutationAsync(
                new(access.UserId, now), action, before, ToAudit(endpoint, false, false, pageAuditState), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(endpoint.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RollBackConcurrencyAsync(transaction, cancellationToken);
        }
    }

    private async Task<RegistryMutationResult> ChangeStateAsync(
        RegistryVersionCommand command,
        RegistryAccessContext access,
        EndpointAuditAction action,
        CancellationToken cancellationToken)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return Forbidden();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var endpoint = await dbContext.Endpoints.Include(candidate => candidate.Monitors)
            .Include(candidate => candidate.Environment)
            .SingleOrDefaultAsync(candidate => candidate.Id == command.EntityId, cancellationToken);
        if (endpoint is null)
        {
            return NotFound();
        }

        var stateError = ValidateState(endpoint, action);
        if (stateError is not null)
        {
            return Validation(stateError);
        }

        dbContext.Entry(endpoint).Property(candidate => candidate.Version).OriginalValue = command.Version;
        var now = DateTimeOffset.UtcNow;
        var pageAuditState = await PageAuditConfiguration.ReadAsync(
            dbContext, endpoint.Id, cancellationToken);
        var before = ToAudit(endpoint, false, false, pageAuditState);
        ApplyState(endpoint, action, endpoint.Environment.IsProduction, access.UserId, now);

        try
        {
            await auditTrail.RecordEndpointMutationAsync(
                new(access.UserId, now), action, before, ToAudit(endpoint, false, false, pageAuditState), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(endpoint.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await RollBackConcurrencyAsync(transaction, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            return await RollBackDuplicateAsync(
                transaction, endpoint.EnvironmentId, endpoint.NormalizedUrl, endpoint.NormalizedUrlHash, cancellationToken);
        }
    }

    private async Task<WebsiteEnvironment?> LockEnvironmentAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var environment = await hierarchyLock.LockEnvironmentAsync(environmentId, cancellationToken);
        if (environment is not { DeletedAt: null })
        {
            return null;
        }

        var website = await hierarchyLock.LockWebsiteAsync(environment.WebsiteId, cancellationToken);
        return website is { DeletedAt: null } ? environment : null;
    }

    private Task<bool> IsValidOwnerAsync(Guid? ownerId, Guid? retainedOwnerId, CancellationToken cancellationToken) =>
        ownerId is null
            ? Task.FromResult(true)
            : mutationSupport.LockValidOwnerAsync(ownerId.Value, retainedOwnerId, cancellationToken);

    private static HttpExceptionDecision DecideHttpException(
        string normalizedUrl,
        string? submittedReason,
        bool isProduction,
        RegistryAccessContext access,
        Endpoint? existing)
    {
        if (!normalizedUrl.StartsWith("http://", StringComparison.Ordinal) || !isProduction)
        {
            return new(null, null, null, null);
        }

        var reason = submittedReason?.Trim();
        if (reason?.Length > 500)
        {
            return new(null, null, null, "This reason is longer than 500 characters. Shorten it.");
        }

        var canRetain = existing is not null
            && string.Equals(existing.NormalizedUrl, normalizedUrl, StringComparison.Ordinal)
            && string.Equals(existing.HttpExceptionReason, reason, StringComparison.Ordinal)
            && existing.HttpExceptionApprovedByUserId is not null
            && existing.HttpExceptionApprovedAt is not null;
        if (canRetain)
        {
            return new(reason, existing!.HttpExceptionApprovedByUserId, existing.HttpExceptionApprovedAt, null);
        }

        if (!access.Roles.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal))
        {
            return new(null, null, null, "Only an Administrator can approve plain HTTP for a Production endpoint. "
                + "Ask one to record the exception, or use an https:// URL.");
        }

        return string.IsNullOrWhiteSpace(reason)
            ? new(null, null, null, "This Production endpoint uses plain http://, which needs a written reason. "
                + "Enter one, or change the URL to https://.")
            : new(reason, access.UserId, DateTimeOffset.UtcNow, null);
    }

    private static Endpoint CreateEndpointEntity(
        CreateEndpoint command,
        Guid actorId,
        EndpointUrlNormalizationResult url,
        HttpExceptionDecision exception,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            EnvironmentId = command.EnvironmentId,
            OwnerSubjectId = command.OwnerSubjectId,
            DisplayUrl = url.DisplayUrl!,
            NormalizedUrl = url.NormalizedUrl!,
            NormalizedUrlHash = url.NormalizedUrlHash!,
            NormalizedHost = url.NormalizedHost!,
            EffectivePort = url.EffectivePort!.Value,
            NormalizationVersion = EndpointUrlNormalizer.Version,
            IsEnabled = command.IsEnabled,
            HttpExceptionReason = exception.Reason,
            HttpExceptionApprovedByUserId = exception.ApprovedByUserId,
            HttpExceptionApprovedAt = exception.ApprovedAt,
            SeoExpectedCanonicalHost = NormalizeExpectedHost(command.SeoExpectedCanonicalHost),
            SeoIndexingExpectation = command.SeoIndexingExpectation,
            SeoDescriptionRequired = command.SeoDescriptionRequired,
            CreatedAt = now,
            CreatedByUserId = actorId,
            UpdatedAt = now,
            UpdatedByUserId = actorId,
            Version = 1
        };

    private static string? NormalizeExpectedHost(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static IEnumerable<ValidationError> HttpPolicyErrors(IEnumerable<ValidationError> errors) => errors.Select(error =>
        error with
        {
            Field = error.Field switch
            {
                "WarningThresholdMs" => nameof(UpdateEndpoint.WarningThresholdMsOverride),
                "CriticalThresholdMs" => nameof(UpdateEndpoint.CriticalThresholdMsOverride),
                "IntervalSeconds" => nameof(UpdateEndpoint.IntervalMinutesOverride),
                _ => "HttpPolicy." + error.Field
            }
        });

    private static HttpMonitorOverridesV2 BuildHttpOverrides(
        HttpMonitorOverridesV2? policy, int? intervalSeconds, int? warningMs, int? criticalMs) =>
        (policy ?? new HttpMonitorOverridesV2()) with
        {
            IntervalSeconds = intervalSeconds,
            WarningThresholdMs = warningMs,
            CriticalThresholdMs = criticalMs
        };

    private static EndpointCreatePreparation PrepareCreate(
        CreateEndpoint command,
        RegistryAccessContext access)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return new(null, null, null, Forbidden());
        }

        var url = EndpointUrlNormalizer.Normalize(command.Url);
        if (!url.Succeeded)
        {
            return new(null, null, null, Validation(url.Errors.Select(error =>
                ValidationError.For(nameof(CreateEndpoint.Url), error))));
        }

        if (DestinationHostPolicy.IsDefinitelyUnreachable(url.NormalizedHost, out var unreachable))
        {
            return new(null, null, null,
                Validation(ValidationError.For(nameof(CreateEndpoint.Url), unreachable!)));
        }

        var interval = DecideIntervalOverride(command.IntervalMinutesOverride, access, null);
        if (interval.Error is not null)
        {
            return new(null, null, null, Validation(ValidationError.For(
                nameof(UpdateEndpoint.IntervalMinutesOverride), interval.Error)));
        }

        var thresholds = ResponseThresholdOverride.Decide(
            command.WarningThresholdMsOverride, command.CriticalThresholdMsOverride);
        if (thresholds.Error is not null)
        {
            return new(null, null, null, Validation(ValidationError.For(
                nameof(UpdateEndpoint.WarningThresholdMsOverride), thresholds.Error)));
        }

        if (ValidateSeoPolicy(command.SeoIndexingExpectation, command.SeoExpectedCanonicalHost) is { } seoError)
        {
            return new(null, null, null, Validation(ValidationError.For(
                nameof(UpdateEndpoint.SeoIndexingExpectation), seoError)));
        }

        if (PageAuditConfiguration.Validate(
            command.PageAuditEnabled,
            command.PageAuditSchedulingEnabled,
            command.PageAuditIntervalHours,
            url.NormalizedUrl!) is { } pageAuditError)
        {
            return new(null, null, null, Validation(ValidationError.For(
                nameof(UpdateEndpoint.PageAuditIntervalHours), pageAuditError)));
        }

        return new(url, interval, thresholds, null);
    }

    private static string? ValidateSeoPolicy(string expectation, string? expectedHost)
    {
        if (!SeoIndexingExpectations.IsSupported(expectation))
        {
            return "Select an indexing expectation from the list.";
        }

        var host = NormalizeExpectedHost(expectedHost);
        return host is not null && (host.Length > 253 || Uri.CheckHostName(host) == UriHostNameType.Unknown)
            ? "Enter a host name only, such as www.example.com — no scheme, port or path."
            : null;
    }

    private static EndpointMonitor AvailabilityMonitor(Endpoint endpoint) =>
        endpoint.Monitors.Single(candidate =>
            candidate.DeletedAt == null
            && candidate.MonitorType == RegistryDefaults.HttpAvailabilityMonitorType);

    private static EndpointMonitor AuditedAvailabilityMonitor(Endpoint endpoint) =>
        endpoint.Monitors
            .Where(candidate => candidate.MonitorType == RegistryDefaults.HttpAvailabilityMonitorType)
            .OrderBy(candidate => candidate.DeletedAt is null ? 0 : 1)
            .ThenByDescending(candidate => candidate.CreatedAt)
            .ThenBy(candidate => candidate.Id)
            .First();

    private void ApplySslMonitorPresence(
        Endpoint endpoint,
        bool isProduction,
        bool schedulingEnabled,
        Guid actorId,
        DateTimeOffset now,
        bool tlsIdentityChanged = false)
    {
        EndpointMonitorReconciler.ReconcileSsl(
            dbContext, endpoint, isProduction, schedulingEnabled, actorId, now, tlsIdentityChanged);
    }

    private static void ApplySslMonitorUpdate(
        EndpointMonitor monitor,
        Endpoint endpoint,
        bool schedulingEnabled,
        bool isProduction,
        Guid actorId,
        DateTimeOffset now)
    {
        if (monitor.SchedulingEnabled != schedulingEnabled)
        {
            monitor.SchedulingEnabled = schedulingEnabled;
            if (schedulingEnabled)
            {
                monitor.IsEnabled = true;
                monitor.NextDueAt = MonitorCadence.GetResumeDueAt(monitor.NextDueAt, now);
            }
        }

        monitor.ConfigurationFingerprint = RegistryDefaults.CreateSslFingerprint(
            endpoint.NormalizedUrl, isProduction);
        monitor.UpdatedAt = now;
        monitor.UpdatedByUserId = actorId;
        monitor.Version++;
    }

    private void ApplyPageAuditMonitor(
        Endpoint endpoint,
        bool isProduction,
        bool enabled,
        Guid actorId,
        DateTimeOffset now)
    {
        var monitor = endpoint.Monitors.SingleOrDefault(candidate =>
                candidate.DeletedAt == null
                && candidate.MonitorType == RegistryDefaults.PageAuditMonitorType)
            ?? dbContext.EndpointMonitors.Local.SingleOrDefault(candidate =>
                candidate.EndpointId == endpoint.Id
                && candidate.DeletedAt == null
                && candidate.MonitorType == RegistryDefaults.PageAuditMonitorType);
        if (monitor is null)
        {
            if (!enabled)
            {
                return;
            }

            dbContext.EndpointMonitors.Add(new EndpointMonitor
            {
                Id = Guid.NewGuid(),
                EndpointId = endpoint.Id,
                PolicyProfileId = RegistryDefaults.PageAuditPolicyProfileId,
                MonitorType = RegistryDefaults.PageAuditMonitorType,
                BoundedOverrides = "{}",
                ScheduleAnchor = now,
                NextDueAt = now.AddSeconds(RegistryDefaults.PageAuditIntervalSeconds),
                ConfigurationFingerprint = RegistryDefaults.CreatePageAuditFingerprint(
                    endpoint.NormalizedUrl, isProduction),
                IntervalSeconds = RegistryDefaults.PageAuditIntervalSeconds,
                TimeoutSeconds = RegistryDefaults.PageAuditTimeoutSeconds,
                FailureConfirmationCount = RegistryDefaults.PageAuditFailureConfirmationCount,
                RecoveryConfirmationCount = RegistryDefaults.PageAuditRecoveryConfirmationCount,
                SchedulingEnabled = false,
                IsEnabled = true,
                CreatedAt = now,
                CreatedByUserId = actorId,
                UpdatedAt = now,
                UpdatedByUserId = actorId,
                Version = 1
            });
            return;
        }

        var fingerprint = RegistryDefaults.CreatePageAuditFingerprint(endpoint.NormalizedUrl, isProduction);
        if (monitor.IsEnabled == enabled && monitor.ConfigurationFingerprint == fingerprint)
        {
            return;
        }

        monitor.IsEnabled = enabled;
        monitor.ConfigurationFingerprint = fingerprint;
        monitor.UpdatedAt = now;
        monitor.UpdatedByUserId = actorId;
        monitor.Version++;
    }

    private void ApplyEndpointUpdate(
        Endpoint endpoint,
        UpdateEndpoint command,
        EndpointUrlNormalizationResult url,
        HttpExceptionDecision exception,
        bool isProduction,
        int? intervalOverrideSeconds,
        ResponseTimeThresholds thresholds,
        Guid actorId,
        DateTimeOffset now)
    {
        var tlsIdentityChanged = endpoint.NormalizedHost != url.NormalizedHost
            || endpoint.EffectivePort != url.EffectivePort!.Value;
        endpoint.OwnerSubjectId = command.OwnerSubjectId;
        endpoint.DisplayUrl = url.DisplayUrl!;
        endpoint.NormalizedUrl = url.NormalizedUrl!;
        endpoint.NormalizedUrlHash = url.NormalizedUrlHash!;
        endpoint.NormalizedHost = url.NormalizedHost!;
        endpoint.EffectivePort = url.EffectivePort!.Value;
        endpoint.NormalizationVersion = EndpointUrlNormalizer.Version;
        endpoint.IsEnabled = command.IsEnabled;
        endpoint.HttpExceptionReason = exception.Reason;
        endpoint.HttpExceptionApprovedByUserId = exception.ApprovedByUserId;
        endpoint.HttpExceptionApprovedAt = exception.ApprovedAt;
        endpoint.SeoExpectedCanonicalHost = NormalizeExpectedHost(command.SeoExpectedCanonicalHost);
        endpoint.SeoIndexingExpectation = command.SeoIndexingExpectation;
        endpoint.SeoDescriptionRequired = command.SeoDescriptionRequired;
        Touch(endpoint, actorId, now);
        ApplySslMonitorPresence(
            endpoint, isProduction, command.SchedulingEnabled, actorId, now, tlsIdentityChanged);
        foreach (var monitor in endpoint.Monitors.Where(monitor => monitor.DeletedAt == null))
        {
            switch (monitor.MonitorType)
            {
                case RegistryDefaults.SslCertificateMonitorType:
                    ApplySslMonitorUpdate(monitor, endpoint, command.SchedulingEnabled, isProduction, actorId, now);
                    continue;
                case RegistryDefaults.PageAuditMonitorType:
                    continue;
                case RegistryDefaults.HttpAvailabilityMonitorType:
                    break;
                default:
                    throw new InvalidOperationException("Unsupported monitor type for endpoint update.");
            }

            var resumeDueAt = !monitor.SchedulingEnabled && command.SchedulingEnabled
                ? MonitorCadence.GetResumeDueAt(monitor.NextDueAt, now)
                : (DateTimeOffset?)null;
            var interval = intervalOverrideSeconds ?? RegistryDefaults.GetHttpIntervalSeconds(isProduction);
            if (monitor.IntervalSeconds != interval)
            {
                monitor.IntervalSeconds = interval;
                monitor.NextDueAt = MonitorCadence.GetFirstSlotAfter(
                    monitor.ScheduleAnchor, interval, now);
            }

            if (monitor.SchedulingEnabled != command.SchedulingEnabled)
            {
                monitor.SchedulingEnabled = command.SchedulingEnabled;
                if (command.SchedulingEnabled)
                {
                    monitor.IsEnabled = true;
                    monitor.NextDueAt = resumeDueAt!.Value;
                }
            }

            monitor.UpdatedAt = now;
            monitor.UpdatedByUserId = actorId;
            monitor.Version++;
        }
    }

    private void ApplyState(
        Endpoint endpoint,
        EndpointAuditAction action,
        bool isProduction,
        Guid actorId,
        DateTimeOffset now)
    {
        endpoint.IsEnabled = false;
        if (action == EndpointAuditAction.Deleted)
        {
            EndpointMonitorReconciler.Archive(endpoint, actorId, now);
            endpoint.DeletedAt = now;
            endpoint.DeletedByUserId = actorId;
        }
        else if (action == EndpointAuditAction.Restored)
        {
            EndpointMonitorReconciler.Restore(dbContext, endpoint, isProduction, actorId, now);
            endpoint.DeletedAt = null;
            endpoint.DeletedByUserId = null;
        }

        Touch(endpoint, actorId, now);
    }

    private static void Touch(Endpoint endpoint, Guid actorId, DateTimeOffset now)
    {
        endpoint.UpdatedAt = now;
        endpoint.UpdatedByUserId = actorId;
        endpoint.Version++;
    }

    private static EndpointAuditSnapshot ToAudit(
        Endpoint endpoint,
        bool urlChanged,
        bool httpExceptionChanged,
        PageAuditConfigurationState pageAudit)
    {
        var monitor = AuditedAvailabilityMonitor(endpoint);
        var policy = HttpMonitorConfiguration.ReadOverrides(monitor);
        return new(
            endpoint.Id, endpoint.EnvironmentId, endpoint.OwnerSubjectId,
            Convert.ToHexString(endpoint.NormalizedUrlHash).ToLowerInvariant(), endpoint.NormalizationVersion,
            urlChanged, endpoint.IsEnabled, endpoint.HttpExceptionReason is not null,
            httpExceptionChanged, monitor.IntervalSeconds,
            MonitorIntervalOverride.HasOverride(monitor.BoundedOverrides),
            endpoint.SeoIndexingExpectation,
            endpoint.SeoDescriptionRequired,
            endpoint.SeoExpectedCanonicalHost is not null,
            pageAudit.Enabled,
            pageAudit.SchedulingEnabled,
            endpoint.DeletedAt is not null, endpoint.Version,
            new(monitor.TimeoutSeconds, monitor.FailureConfirmationCount, monitor.RecoveryConfirmationCount,
                monitor.WarningThresholdMs, monitor.CriticalThresholdMs, policy.AdditionalAcceptedStatusCodes ?? [],
                !string.IsNullOrEmpty(policy.RequiredContentMarker), policy.ContentMarkerComparison ?? "OrdinalIgnoreCase"));
    }

    private static IntervalOverrideDecision DecideIntervalOverride(
        int? intervalMinutes,
        RegistryAccessContext access,
        int? currentSeconds)
    {
        if (intervalMinutes is < 1 or > 1440)
        {
            return new(null, "Enter a monitoring interval between 1 and 1440 minutes, or leave it blank "
                + "to use the default for this environment.");
        }

        int? submittedSeconds = intervalMinutes * 60;

        if (submittedSeconds != currentSeconds
            && !access.Roles.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal))
        {
            return new(null, "Only an Administrator can change the monitoring interval. Leave it blank to "
                + "keep the default for this environment.");
        }

        return new(submittedSeconds, null);
    }

    private static string? ValidateState(Endpoint endpoint, EndpointAuditAction action) => action switch
    {
        EndpointAuditAction.Disabled when endpoint.DeletedAt is not null => "Restore the endpoint before disabling it.",
        EndpointAuditAction.Deleted when endpoint.DeletedAt is not null => "The endpoint is already deleted.",
        EndpointAuditAction.Restored when endpoint.DeletedAt is null => "The endpoint is not deleted.",
        _ => null
    };

    private static bool IsDuplicate(DbUpdateException exception) =>
        RegistryMutationSupport.IsConstraintViolation(exception, EndpointUrlIndex);

    private async Task<RegistryMutationResult> RollBackDuplicateAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        Guid environmentId,
        string normalizedUrl,
        byte[] normalizedUrlHash,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return await ResolveEndpointUrlDuplicateAsync(
            new EndpointUrlDuplicate(environmentId, normalizedUrl, normalizedUrlHash),
            cancellationToken);
    }

    internal async Task<RegistryMutationResult> ResolveEndpointUrlDuplicateAsync(
        EndpointUrlDuplicate duplicate,
        CancellationToken cancellationToken = default)
    {
        var existingUrl = await dbContext.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.EnvironmentId == duplicate.EnvironmentId
                && endpoint.DeletedAt == null
                && endpoint.NormalizationVersion == EndpointUrlNormalizer.Version
                && endpoint.NormalizedUrlHash.SequenceEqual(duplicate.NormalizedUrlHash))
            .Select(endpoint => endpoint.NormalizedUrl)
            .SingleOrDefaultAsync(cancellationToken);
        return string.Equals(existingUrl, duplicate.NormalizedUrl, StringComparison.Ordinal)
            ? Validation(ValidationError.For(
                nameof(CreateEndpoint.Url),
                "This environment already has an endpoint at this URL. URLs are compared after "
                + "normalization, so a differently written address can still be the same endpoint."))
            : Validation(ValidationError.For(
                nameof(CreateEndpoint.Url),
                "This URL could not be stored because its identity clashes with an existing one. "
                + "Nothing was saved. Report this with the URL you entered."));
    }

    private async Task<RegistryMutationResult> RollBackConcurrencyAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return RegistryMutationResult.Failure(RegistryMutationStatus.ConcurrencyConflict,
            "This endpoint changed after you opened it. Return to details and reopen the edit form.");
    }

    private static RegistryMutationResult Forbidden() => RegistryMutationResult.Failure(RegistryMutationStatus.Forbidden, "Registry management is not permitted.");
    private static RegistryMutationResult NotFound() => RegistryMutationResult.Failure(RegistryMutationStatus.NotFound, "The endpoint was not found.");
    private static RegistryMutationResult Validation(params IEnumerable<ValidationError> errors) => RegistryMutationResult.Failure(RegistryMutationStatus.ValidationFailed, errors);

    private sealed record HttpExceptionDecision(
        string? Reason, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt, string? Error);

    private sealed record IntervalOverrideDecision(int? Seconds, string? Error);

    private sealed record EndpointCreatePreparation(
        EndpointUrlNormalizationResult? Url,
        IntervalOverrideDecision? Interval,
        ResponseThresholdDecision? Thresholds,
        RegistryMutationResult? Failure);
}
