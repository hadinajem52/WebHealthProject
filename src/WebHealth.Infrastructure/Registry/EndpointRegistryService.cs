using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Auditing;
using WebHealth.Application.Registry;
using WebHealth.Domain.Normalization;
using WebHealth.Domain.Monitoring;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class EndpointRegistryService(
    ApplicationDbContext dbContext,
    RegistryMutationSupport mutationSupport,
    IAuditTrailWriter auditTrail) : IEndpointRegistryService
{
    private const string EndpointUrlIndex = "ux_endpoint_environment_url_hash_version_active";

    public async Task<RegistryMutationResult> CreateAsync(
        CreateEndpoint command,
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
            return Validation(url.Errors);
        }

        var interval = DecideIntervalOverride(command.IntervalMinutesOverride, access, null);
        if (interval.Error is not null)
        {
            return Validation(interval.Error);
        }

        var thresholds = ResponseThresholdOverride.Decide(
            command.WarningThresholdMsOverride, command.CriticalThresholdMsOverride);
        if (thresholds.Error is not null)
        {
            return Validation(thresholds.Error);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var environment = await LockEnvironmentAsync(command.EnvironmentId, cancellationToken);
        if (environment is null)
        {
            return Validation("Select an active environment under a non-deleted website.");
        }

        if (!await IsValidOwnerAsync(command.OwnerSubjectId, null, cancellationToken))
        {
            return Validation("Select an enabled user or team owner, or inherit the website owner.");
        }

        var exception = DecideHttpException(url.NormalizedUrl!, command.HttpExceptionReason, environment.IsProduction, access, null);
        if (exception.Error is not null)
        {
            return Validation(exception.Error);
        }

        var now = DateTimeOffset.UtcNow;
        var authorization = DecideTargetAuthorization(
            command.TargetAuthorizationKind, command.TargetAuthorizationEvidence,
            command.TargetAuthorizationExpiresAt, command.IsEnabled, url, null, now);
        if (authorization.Error is not null)
        {
            return Validation(authorization.Error);
        }

        var endpoint = CreateEndpointEntity(command, access.UserId, url, exception, now);
        dbContext.Endpoints.Add(endpoint);
        dbContext.EndpointMonitors.Add(CreateMonitor(
            endpoint, environment.IsProduction, interval.Seconds, command.SchedulingEnabled,
            thresholds.Thresholds, access.UserId, now));
        if (RegistryDefaults.RequiresSslMonitor(endpoint.NormalizedUrl))
        {
            dbContext.EndpointMonitors.Add(CreateSslMonitor(
                endpoint, environment.IsProduction, command.SchedulingEnabled, access.UserId, now));
        }
        await ApplyTargetAuthorizationAsync(endpoint, authorization, access.UserId, now, cancellationToken);

        try
        {
            await auditTrail.RecordEndpointMutationAsync(
                new(access.UserId, now), EndpointAuditAction.Created, null,
                ToAudit(endpoint, urlChanged: true, httpExceptionChanged: exception.Reason is not null,
                    targetAuthorizationChanged: authorization.Changed, now),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RegistryMutationResult.Success(endpoint.Id);
        }
        catch (DbUpdateException exceptionError) when (IsDuplicate(exceptionError))
        {
            return await RollBackDuplicateAsync(
                transaction, command.EnvironmentId, url.NormalizedUrl!, url.NormalizedUrlHash!, cancellationToken);
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
            return Validation(url.Errors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var endpoint = await dbContext.Endpoints.Include(candidate => candidate.Environment)
            .ThenInclude(environment => environment.Website)
            .Include(candidate => candidate.Monitors)
            .Include(candidate => candidate.TargetAuthorizations)
            .SingleOrDefaultAsync(candidate => candidate.Id == command.EndpointId, cancellationToken);
        if (endpoint is null)
        {
            return NotFound();
        }

        if (endpoint.DeletedAt is not null)
        {
            return Validation("Restore the endpoint before editing it.");
        }

        var monitor = AvailabilityMonitor(endpoint);
        var currentIntervalOverride = MonitorIntervalOverride.GetSeconds(monitor.BoundedOverrides);
        var interval = DecideIntervalOverride(
            command.IntervalMinutesOverride, access, currentIntervalOverride);
        if (interval.Error is not null)
        {
            return Validation(interval.Error);
        }

        var thresholds = ResponseThresholdOverride.Decide(
            command.WarningThresholdMsOverride, command.CriticalThresholdMsOverride);
        if (thresholds.Error is not null)
        {
            return Validation(thresholds.Error);
        }

        if (!await IsValidOwnerAsync(command.OwnerSubjectId, endpoint.OwnerSubjectId, cancellationToken))
        {
            return Validation("Select an enabled user or team owner, or inherit the website owner.");
        }

        var exception = DecideHttpException(url.NormalizedUrl!, command.HttpExceptionReason,
            endpoint.Environment.IsProduction, access, endpoint);
        if (exception.Error is not null)
        {
            return Validation(exception.Error);
        }

        dbContext.Entry(endpoint).Property(candidate => candidate.Version).OriginalValue = command.Version;
        var urlChanged = !string.Equals(endpoint.NormalizedUrl, url.NormalizedUrl, StringComparison.Ordinal);
        var exceptionChanged = !string.Equals(endpoint.HttpExceptionReason, exception.Reason, StringComparison.Ordinal)
            || endpoint.HttpExceptionApprovedByUserId != exception.ApprovedByUserId;
        var now = DateTimeOffset.UtcNow;
        var currentAuthorization = endpoint.TargetAuthorizations.SingleOrDefault(evidence =>
            evidence.RevokedAt == null
            && evidence.NormalizedHost == endpoint.NormalizedHost
            && evidence.Port == endpoint.EffectivePort);
        var authorization = DecideTargetAuthorization(
            command.TargetAuthorizationKind, command.TargetAuthorizationEvidence,
            command.TargetAuthorizationExpiresAt, command.IsEnabled, url, currentAuthorization, now);
        if (authorization.Error is not null)
        {
            return Validation(authorization.Error);
        }

        var before = ToAudit(endpoint, urlChanged: false, httpExceptionChanged: false,
            targetAuthorizationChanged: false, now);
        try
        {
            await ApplyTargetAuthorizationAsync(endpoint, authorization, access.UserId, now, cancellationToken);
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
            await auditTrail.RecordEndpointMutationAsync(
                new(access.UserId, now), EndpointAuditAction.Updated, before,
                ToAudit(endpoint, urlChanged, exceptionChanged, authorization.Changed, now), cancellationToken);
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
            return Validation("Restore the endpoint before changing its schedule.");
        }

        var monitor = endpoint.Monitors.SingleOrDefault(candidate =>
            candidate.DeletedAt == null
            && candidate.MonitorType == RegistryDefaults.HttpAvailabilityMonitorType);
        if (monitor is null)
        {
            return Validation("The endpoint has no active monitor.");
        }

        if (!monitor.SchedulingEnabled)
        {
            return Validation("This endpoint runs manual checks only. Enable scheduled checks in Edit endpoint first.");
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
        var before = ToAudit(endpoint, false, false, false, now);

        // Pausing an endpoint pauses every monitor on it, certificate checks included: a
        // "paused" endpoint that still raised SSL incidents would not be paused at all.
        foreach (var active in endpoint.Monitors.Where(candidate => candidate.DeletedAt == null))
        {
            active.IsEnabled = scheduleEnabled;
            if (scheduleEnabled)
            {
                // Rejoin the cadence grid rather than firing every slot missed while paused.
                active.NextDueAt = MonitorCadence.GetFirstSlotAfter(
                    active.ScheduleAnchor, active.IntervalSeconds, now);
            }

            active.UpdatedAt = now;
            active.UpdatedByUserId = access.UserId;
            active.Version++;
        }

        Touch(endpoint, access.UserId, now);

        try
        {
            await auditTrail.RecordEndpointMutationAsync(
                new(access.UserId, now), action, before, ToAudit(endpoint, false, false, false, now), cancellationToken);
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
        var before = ToAudit(endpoint, false, false, false, now);
        ApplyState(endpoint, action, endpoint.Environment.IsProduction, access.UserId, now);

        try
        {
            await auditTrail.RecordEndpointMutationAsync(
                new(access.UserId, now), action, before, ToAudit(endpoint, false, false, false, now), cancellationToken);
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
        var environment = await dbContext.Environments.FromSqlInterpolated($"""
            SELECT * FROM web_health.environment WHERE id = {environmentId} FOR SHARE
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (environment is not { DeletedAt: null, IsActive: true })
        {
            return null;
        }

        var websiteExists = await dbContext.Websites.AnyAsync(website =>
            website.Id == environment.WebsiteId && website.DeletedAt == null, cancellationToken);
        return websiteExists ? environment : null;
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
            return new(null, null, null, "The HTTP exception reason cannot exceed 500 characters.");
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
            return new(null, null, null, "Only an Administrator can approve HTTP for a Production endpoint.");
        }

        return string.IsNullOrWhiteSpace(reason)
            ? new(null, null, null, "Enter a required reason for the Production HTTP exception.")
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
            CreatedAt = now,
            CreatedByUserId = actorId,
            UpdatedAt = now,
            UpdatedByUserId = actorId,
            Version = 1
        };

    private static EndpointMonitor CreateMonitor(
        Endpoint endpoint,
        bool isProduction,
        int? intervalOverrideSeconds,
        bool schedulingEnabled,
        ResponseTimeThresholds thresholds,
        Guid actorId,
        DateTimeOffset now)
    {
        var interval = intervalOverrideSeconds ?? RegistryDefaults.GetHttpIntervalSeconds(isProduction);
        var schedule = MonitorCadence.Initialize(now);
        return new EndpointMonitor
        {
            Id = Guid.NewGuid(),
            EndpointId = endpoint.Id,
            PolicyProfileId = RegistryDefaults.HttpAvailabilityPolicyProfileId,
            MonitorType = RegistryDefaults.HttpAvailabilityMonitorType,
            BoundedOverrides = MonitorIntervalOverride.Serialize(intervalOverrideSeconds),
            ConfigurationFingerprint = RegistryDefaults.CreateHttpFingerprint(
                endpoint.NormalizedUrl,
                isProduction,
                interval,
                RegistryDefaults.HttpTimeoutSeconds,
                2,
                2,
                thresholds.WarningMs,
                thresholds.CriticalMs),
            ScheduleAnchor = schedule.Anchor,
            NextDueAt = schedule.NextDueAt,
            IntervalSeconds = interval,
            TimeoutSeconds = RegistryDefaults.HttpTimeoutSeconds,
            FailureConfirmationCount = 2,
            RecoveryConfirmationCount = 2,
            // BR-P02: the resolved value is stored rather than left null, so a result's
            // configuration snapshot records the threshold it was judged against even if the
            // documented default changes later.
            WarningThresholdMs = thresholds.WarningMs,
            CriticalThresholdMs = thresholds.CriticalMs,
            SchedulingEnabled = schedulingEnabled,
            IsEnabled = true,
            CreatedAt = now,
            CreatedByUserId = actorId,
            UpdatedAt = now,
            UpdatedByUserId = actorId,
            Version = 1
        };
    }

    private static EndpointMonitor AvailabilityMonitor(Endpoint endpoint) =>
        endpoint.Monitors.Single(candidate =>
            candidate.DeletedAt == null
            && candidate.MonitorType == RegistryDefaults.HttpAvailabilityMonitorType);

    /// <summary>
    /// The audit snapshot describes the availability monitor whatever its lifecycle state.
    /// Deleting an endpoint retires its monitors before the "after" snapshot is taken, so
    /// requiring a live one here would throw on exactly the mutation being recorded. A live
    /// monitor still wins when there is one.
    /// </summary>
    private static EndpointMonitor AuditedAvailabilityMonitor(Endpoint endpoint) =>
        endpoint.Monitors
            .Where(candidate => candidate.MonitorType == RegistryDefaults.HttpAvailabilityMonitorType)
            .OrderBy(candidate => candidate.DeletedAt is null ? 0 : 1)
            .ThenByDescending(candidate => candidate.CreatedAt)
            .First();

    /// <summary>
    /// BR-C01: the certificate monitor exists exactly while the endpoint is HTTPS. Switching an
    /// endpoint to HTTP retires its certificate monitor instead of leaving one that can never
    /// observe anything, and switching back to HTTPS creates a fresh one.
    /// </summary>
    private static void ApplySslMonitorPresence(
        Endpoint endpoint,
        bool isProduction,
        bool schedulingEnabled,
        Guid actorId,
        DateTimeOffset now,
        bool tlsIdentityChanged = false)
    {
        var existing = endpoint.Monitors.SingleOrDefault(candidate =>
            candidate.DeletedAt == null
            && candidate.MonitorType == RegistryDefaults.SslCertificateMonitorType);
        var required = RegistryDefaults.RequiresSslMonitor(endpoint.NormalizedUrl);

        // A different host or port is a different certificate. Keeping the same monitor would
        // leave the previous host's observations attached to it, so the endpoint page would
        // show the old certificate until the next daily check, and fingerprint-keyed
        // deduplication would compare two unrelated TLS identities.
        if (existing is not null && (!required || tlsIdentityChanged))
        {
            Retire(existing, actorId, now);
            existing = null;
        }

        if (required && existing is null)
        {
            endpoint.Monitors.Add(CreateSslMonitor(endpoint, isProduction, schedulingEnabled, actorId, now));
        }
    }

    private static void Retire(EndpointMonitor monitor, Guid actorId, DateTimeOffset now)
    {
        monitor.IsEnabled = false;
        monitor.DeletedAt = now;
        monitor.DeletedByUserId = actorId;
        monitor.UpdatedAt = now;
        monitor.UpdatedByUserId = actorId;
        monitor.Version++;
    }

    private static void ApplySslMonitorUpdate(
        EndpointMonitor monitor,
        Endpoint endpoint,
        bool schedulingEnabled,
        bool isProduction,
        Guid actorId,
        DateTimeOffset now)
    {
        // The certificate cadence is fixed by BR-C07 and is not affected by the endpoint's
        // availability interval override.
        if (monitor.SchedulingEnabled != schedulingEnabled)
        {
            monitor.SchedulingEnabled = schedulingEnabled;
            if (schedulingEnabled)
            {
                monitor.IsEnabled = true;
                monitor.NextDueAt = MonitorCadence.GetFirstSlotAfter(
                    monitor.ScheduleAnchor, monitor.IntervalSeconds, now);
            }
        }

        monitor.ConfigurationFingerprint = RegistryDefaults.CreateSslFingerprint(
            endpoint.NormalizedUrl, isProduction);
        monitor.UpdatedAt = now;
        monitor.UpdatedByUserId = actorId;
        monitor.Version++;
    }

    private static EndpointMonitor CreateSslMonitor(
        Endpoint endpoint,
        bool isProduction,
        bool schedulingEnabled,
        Guid actorId,
        DateTimeOffset now)
    {
        var schedule = MonitorCadence.Initialize(now);
        return new EndpointMonitor
        {
            Id = Guid.NewGuid(),
            EndpointId = endpoint.Id,
            PolicyProfileId = RegistryDefaults.SslCertificatePolicyProfileId,
            MonitorType = RegistryDefaults.SslCertificateMonitorType,
            BoundedOverrides = MonitorIntervalOverride.Serialize(null),
            ConfigurationFingerprint = RegistryDefaults.CreateSslFingerprint(
                endpoint.NormalizedUrl, isProduction),
            ScheduleAnchor = schedule.Anchor,
            NextDueAt = schedule.NextDueAt,
            IntervalSeconds = RegistryDefaults.SslIntervalSeconds,
            TimeoutSeconds = RegistryDefaults.SslTimeoutSeconds,
            FailureConfirmationCount = RegistryDefaults.SslFailureConfirmationCount,
            RecoveryConfirmationCount = RegistryDefaults.SslRecoveryConfirmationCount,
            WarningThresholdMs = null,
            CriticalThresholdMs = null,
            SchedulingEnabled = schedulingEnabled,
            IsEnabled = true,
            CreatedAt = now,
            CreatedByUserId = actorId,
            UpdatedAt = now,
            UpdatedByUserId = actorId,
            Version = 1
        };
    }

    private static void ApplyEndpointUpdate(
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
        Touch(endpoint, actorId, now);
        ApplySslMonitorPresence(
            endpoint, isProduction, command.SchedulingEnabled, actorId, now, tlsIdentityChanged);
        foreach (var monitor in endpoint.Monitors.Where(monitor => monitor.DeletedAt == null))
        {
            if (monitor.MonitorType == RegistryDefaults.SslCertificateMonitorType)
            {
                ApplySslMonitorUpdate(monitor, endpoint, command.SchedulingEnabled, isProduction, actorId, now);
                continue;
            }

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
                    // Turning scheduling back on clears any earlier pause and rejoins the grid.
                    monitor.IsEnabled = true;
                    monitor.NextDueAt = MonitorCadence.GetFirstSlotAfter(
                        monitor.ScheduleAnchor, interval, now);
                }
            }

            monitor.BoundedOverrides = MonitorIntervalOverride.Serialize(intervalOverrideSeconds);
            monitor.WarningThresholdMs = thresholds.WarningMs;
            monitor.CriticalThresholdMs = thresholds.CriticalMs;
            monitor.ConfigurationFingerprint = RegistryDefaults.CreateHttpFingerprint(
                endpoint.NormalizedUrl,
                isProduction,
                interval,
                monitor.TimeoutSeconds,
                monitor.FailureConfirmationCount,
                monitor.RecoveryConfirmationCount,
                monitor.WarningThresholdMs,
                monitor.CriticalThresholdMs);
            monitor.UpdatedAt = now;
            monitor.UpdatedByUserId = actorId;
            monitor.Version++;
        }
    }

    private static void ApplyState(
        Endpoint endpoint,
        EndpointAuditAction action,
        bool isProduction,
        Guid actorId,
        DateTimeOffset now)
    {
        endpoint.IsEnabled = false;
        if (action == EndpointAuditAction.Deleted)
        {
            endpoint.DeletedAt = now;
            endpoint.DeletedByUserId = actorId;
        }
        else if (action == EndpointAuditAction.Restored)
        {
            endpoint.DeletedAt = null;
            endpoint.DeletedByUserId = null;
        }

        var requiresSslMonitor = RegistryDefaults.RequiresSslMonitor(endpoint.NormalizedUrl);
        foreach (var monitor in endpoint.Monitors)
        {
            if (action == EndpointAuditAction.Deleted)
            {
                monitor.DeletedAt = now;
                monitor.DeletedByUserId = actorId;
            }
            else if (action == EndpointAuditAction.Restored)
            {
                // Restoring reconciles against the endpoint's current URL rather than
                // resurrecting every monitor it ever had. A certificate monitor retired because
                // the endpoint moved to HTTP must stay retired.
                if (monitor.MonitorType == RegistryDefaults.SslCertificateMonitorType
                    && !requiresSslMonitor)
                {
                    continue;
                }

                monitor.DeletedAt = null;
                monitor.DeletedByUserId = null;
            }

            monitor.UpdatedAt = now;
            monitor.UpdatedByUserId = actorId;
            monitor.Version++;
        }

        if (action == EndpointAuditAction.Restored && requiresSslMonitor
            && !endpoint.Monitors.Any(monitor =>
                monitor.DeletedAt == null
                && monitor.MonitorType == RegistryDefaults.SslCertificateMonitorType))
        {
            // An HTTPS endpoint archived before certificate monitoring existed, or one whose
            // certificate monitor was retired for a different URL, gets one on restore.
            var availability = endpoint.Monitors.FirstOrDefault(monitor =>
                monitor.MonitorType == RegistryDefaults.HttpAvailabilityMonitorType);
            endpoint.Monitors.Add(CreateSslMonitor(
                endpoint,
                isProduction,
                availability?.SchedulingEnabled ?? true,
                actorId,
                now));
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
        bool targetAuthorizationChanged,
        DateTimeOffset now)
    {
        var monitor = AuditedAvailabilityMonitor(endpoint);
        return new(
            endpoint.Id, endpoint.EnvironmentId, endpoint.OwnerSubjectId,
            Convert.ToHexString(endpoint.NormalizedUrlHash).ToLowerInvariant(), endpoint.NormalizationVersion,
            urlChanged, endpoint.IsEnabled, endpoint.HttpExceptionReason is not null,
            httpExceptionChanged, HasCurrentAuthorization(endpoint, now), targetAuthorizationChanged,
            monitor.IntervalSeconds,
            MonitorIntervalOverride.HasOverride(monitor.BoundedOverrides),
            endpoint.DeletedAt is not null, endpoint.Version);
    }

    private static IntervalOverrideDecision DecideIntervalOverride(
        int? intervalMinutes,
        RegistryAccessContext access,
        int? currentSeconds)
    {
        if (intervalMinutes is < 1 or > 1440)
        {
            return new(null, "The monitoring interval must be between 1 minute and 24 hours.");
        }

        int? submittedSeconds = intervalMinutes * 60;

        if (submittedSeconds != currentSeconds
            && !access.Roles.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal))
        {
            return new(null, "Only an Administrator can change the monitoring interval.");
        }

        return new(submittedSeconds, null);
    }

    private static bool HasCurrentAuthorization(Endpoint endpoint, DateTimeOffset now) =>
        endpoint.TargetAuthorizations.Any(evidence =>
            evidence.RevokedAt == null
            && evidence.EffectiveFrom <= now
            && (evidence.ExpiresAt == null || evidence.ExpiresAt > now)
            && evidence.NormalizedHost == endpoint.NormalizedHost
            && evidence.Port == endpoint.EffectivePort);

    private static TargetAuthorizationDecision DecideTargetAuthorization(
        string? submittedKind,
        string? submittedEvidence,
        DateTimeOffset? expiresAt,
        bool endpointEnabled,
        EndpointUrlNormalizationResult url,
        TargetAuthorizationEvidence? current,
        DateTimeOffset now)
    {
        var kind = submittedKind?.Trim();
        var evidence = submittedEvidence?.Trim();
        if (string.IsNullOrEmpty(kind) && string.IsNullOrEmpty(evidence) && expiresAt is null)
        {
            return endpointEnabled
                ? new(null, current, false, "Enabled endpoints require ownership or explicit testing-permission evidence.")
                : new(null, current, current is not null, null);
        }

        if (!TargetAuthorizationKinds.All.Contains(kind, StringComparer.Ordinal))
        {
            return new(null, current, false, "Select owned target or explicit testing permission.");
        }

        if (string.IsNullOrWhiteSpace(evidence) || evidence.Length > 500)
        {
            return new(null, current, false, "Enter a target-authorization reference of at most 500 characters.");
        }

        if (expiresAt is not null && expiresAt <= now)
        {
            return new(null, current, false, "Target authorization must expire in the future.");
        }

        var unchanged = current is not null
            && current.NormalizedHost == url.NormalizedHost
            && current.Port == url.EffectivePort
            && current.AuthorizationKind == kind
            && current.EvidenceReference == evidence
            && current.ExpiresAt == expiresAt;
        if (unchanged)
        {
            return new(null, null, false, null);
        }

        return new(new TargetAuthorizationEvidence
        {
            Id = Guid.NewGuid(),
            AuthorizationKind = kind!,
            EvidenceReference = evidence,
            NormalizedHost = url.NormalizedHost!,
            Port = url.EffectivePort!.Value,
            EffectiveFrom = now,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            Version = 1
        }, current, true, null);
    }

    private async Task ApplyTargetAuthorizationAsync(
        Endpoint endpoint,
        TargetAuthorizationDecision decision,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (decision is { ToCreate: not null, ToRevoke: not null }
            && decision.ToCreate.NormalizedHost == decision.ToRevoke.NormalizedHost
            && decision.ToCreate.Port == decision.ToRevoke.Port)
        {
            decision.ToRevoke.AuthorizationKind = decision.ToCreate.AuthorizationKind;
            decision.ToRevoke.EvidenceReference = decision.ToCreate.EvidenceReference;
            decision.ToRevoke.ExpiresAt = decision.ToCreate.ExpiresAt;
            decision.ToRevoke.Version++;
            return;
        }

        if (decision.ToRevoke is not null)
        {
            decision.ToRevoke.RevokedAt = now;
            decision.ToRevoke.RevokedByUserId = actorId;
            decision.ToRevoke.RevocationReason = "Endpoint authorization evidence replaced or removed.";
            decision.ToRevoke.Version++;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (decision.ToCreate is not null)
        {
            decision.ToCreate.EndpointId = endpoint.Id;
            decision.ToCreate.CreatedByUserId = actorId;
            endpoint.TargetAuthorizations.Add(decision.ToCreate);
        }
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
        var existingUrl = await dbContext.Endpoints.AsNoTracking()
            .Where(endpoint => endpoint.EnvironmentId == environmentId
                && endpoint.DeletedAt == null
                && endpoint.NormalizationVersion == EndpointUrlNormalizer.Version
                && endpoint.NormalizedUrlHash.SequenceEqual(normalizedUrlHash))
            .Select(endpoint => endpoint.NormalizedUrl)
            .SingleOrDefaultAsync(cancellationToken);
        return string.Equals(existingUrl, normalizedUrl, StringComparison.Ordinal)
            ? Validation("An endpoint with this normalized URL already exists in the environment.")
            : Validation("A URL identity hash collision was detected. No endpoint was saved.");
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
    private static RegistryMutationResult Validation(params IEnumerable<string> errors) => RegistryMutationResult.Failure(RegistryMutationStatus.ValidationFailed, errors);

    private sealed record HttpExceptionDecision(
        string? Reason, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt, string? Error);

    private sealed record IntervalOverrideDecision(int? Seconds, string? Error);

    private sealed record TargetAuthorizationDecision(
        TargetAuthorizationEvidence? ToCreate,
        TargetAuthorizationEvidence? ToRevoke,
        bool Changed,
        string? Error);
}
