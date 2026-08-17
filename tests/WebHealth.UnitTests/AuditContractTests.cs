using FluentAssertions;
using WebHealth.Application.Auditing;
using Xunit;

namespace WebHealth.UnitTests;

public sealed class AuditContractTests
{
    [Fact]
    public void MutationWriter_ExposesOnlyTypedAllowListedOperations()
    {
        var methods = typeof(IAuditTrailWriter).GetMethods();

        methods.Select(method => method.Name).Should().BeEquivalentTo(
            nameof(IAuditTrailWriter.RecordUserCreatedAsync),
            nameof(IAuditTrailWriter.RecordUserUpdatedAsync),
            nameof(IAuditTrailWriter.RecordTeamCreatedAsync),
            nameof(IAuditTrailWriter.RecordTeamUpdatedAsync),
            nameof(IAuditTrailWriter.RecordClientMutationAsync),
            nameof(IAuditTrailWriter.RecordWebsiteMutationAsync),
            nameof(IAuditTrailWriter.RecordEnvironmentMutationAsync),
            nameof(IAuditTrailWriter.RecordEndpointMutationAsync),
            nameof(IAuditTrailWriter.RecordMaintenanceMutationAsync),
            nameof(IAuditTrailWriter.RecordIncidentMutationAsync));
        methods.SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should().OnlyContain(type =>
                type == typeof(AuditWriteContext)
                || type == typeof(UserAuditSnapshot)
                || type == typeof(TeamAuditSnapshot)
                || type == typeof(ClientAuditAction)
                || type == typeof(ClientAuditSnapshot)
                || type == typeof(WebsiteAuditAction)
                || type == typeof(WebsiteAuditSnapshot)
                || type == typeof(EnvironmentAuditAction)
                || type == typeof(EnvironmentAuditSnapshot)
                || type == typeof(EndpointAuditAction)
                || type == typeof(EndpointAuditSnapshot)
                || type == typeof(MaintenanceAuditAction)
                || type == typeof(MaintenanceAuditSnapshot)
                || type == typeof(IncidentAuditWriteContext)
                || type == typeof(IncidentAuditAction)
                || type == typeof(IncidentAuditSnapshot)
                || type == typeof(CancellationToken));
        typeof(UserAuditSnapshot).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(UserAuditSnapshot.UserId),
                nameof(UserAuditSnapshot.DisplayName),
                nameof(UserAuditSnapshot.Email),
                nameof(UserAuditSnapshot.IsDisabled),
                nameof(UserAuditSnapshot.Roles),
                nameof(UserAuditSnapshot.PasswordReset));
        typeof(TeamAuditSnapshot).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(TeamAuditSnapshot.TeamId),
                nameof(TeamAuditSnapshot.Name),
                nameof(TeamAuditSnapshot.IsDisabled),
                nameof(TeamAuditSnapshot.MemberUserIds));
        typeof(ClientAuditSnapshot).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(ClientAuditSnapshot.ClientId),
                nameof(ClientAuditSnapshot.Name),
                nameof(ClientAuditSnapshot.OwnerSubjectId),
                nameof(ClientAuditSnapshot.IsActive),
                nameof(ClientAuditSnapshot.IsDeleted),
                nameof(ClientAuditSnapshot.NotesChanged),
                nameof(ClientAuditSnapshot.Version));
        typeof(WebsiteAuditSnapshot).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(WebsiteAuditSnapshot.WebsiteId),
                nameof(WebsiteAuditSnapshot.ClientId),
                nameof(WebsiteAuditSnapshot.Name),
                nameof(WebsiteAuditSnapshot.OwnerSubjectId),
                nameof(WebsiteAuditSnapshot.TechnologyCms),
                nameof(WebsiteAuditSnapshot.IsEnabled),
                nameof(WebsiteAuditSnapshot.IsDeleted),
                nameof(WebsiteAuditSnapshot.Version),
                nameof(WebsiteAuditSnapshot.Tags));
        typeof(EnvironmentAuditSnapshot).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(EnvironmentAuditSnapshot.EnvironmentId),
                nameof(EnvironmentAuditSnapshot.WebsiteId),
                nameof(EnvironmentAuditSnapshot.Name),
                nameof(EnvironmentAuditSnapshot.EnvironmentType),
                nameof(EnvironmentAuditSnapshot.IsProduction),
                nameof(EnvironmentAuditSnapshot.BaseUrlChanged),
                nameof(EnvironmentAuditSnapshot.IsActive),
                nameof(EnvironmentAuditSnapshot.IsDeleted),
                nameof(EnvironmentAuditSnapshot.Version));
        typeof(EndpointAuditSnapshot).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(EndpointAuditSnapshot.EndpointId),
                nameof(EndpointAuditSnapshot.EnvironmentId),
                nameof(EndpointAuditSnapshot.OwnerSubjectId),
                nameof(EndpointAuditSnapshot.NormalizedUrlHash),
                nameof(EndpointAuditSnapshot.NormalizationVersion),
                nameof(EndpointAuditSnapshot.UrlChanged),
                nameof(EndpointAuditSnapshot.IsEnabled),
                nameof(EndpointAuditSnapshot.HasHttpException),
                nameof(EndpointAuditSnapshot.HttpExceptionChanged),
                nameof(EndpointAuditSnapshot.HasTargetAuthorization),
                nameof(EndpointAuditSnapshot.TargetAuthorizationChanged),
                nameof(EndpointAuditSnapshot.MonitorIntervalSeconds),
                nameof(EndpointAuditSnapshot.HasIntervalOverride),
                nameof(EndpointAuditSnapshot.IsDeleted),
                nameof(EndpointAuditSnapshot.Version));
        typeof(MaintenanceAuditSnapshot).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(MaintenanceAuditSnapshot.MaintenanceWindowId),
                nameof(MaintenanceAuditSnapshot.ScopeKind),
                nameof(MaintenanceAuditSnapshot.ScopeId),
                nameof(MaintenanceAuditSnapshot.StartsAt),
                nameof(MaintenanceAuditSnapshot.EndsAt),
                nameof(MaintenanceAuditSnapshot.TimezoneId),
                nameof(MaintenanceAuditSnapshot.SuppressionPolicy),
                nameof(MaintenanceAuditSnapshot.PauseEscalation),
                nameof(MaintenanceAuditSnapshot.ContinueFailureCounter),
                nameof(MaintenanceAuditSnapshot.IsCancelled),
                nameof(MaintenanceAuditSnapshot.ReasonChanged),
                nameof(MaintenanceAuditSnapshot.Version));
        typeof(IncidentAuditSnapshot).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(IncidentAuditSnapshot.IncidentId),
                nameof(IncidentAuditSnapshot.EndpointMonitorId),
                nameof(IncidentAuditSnapshot.OwnerSubjectId),
                nameof(IncidentAuditSnapshot.PreviousIncidentId),
                nameof(IncidentAuditSnapshot.IssueKey),
                nameof(IncidentAuditSnapshot.Severity),
                nameof(IncidentAuditSnapshot.Status),
                nameof(IncidentAuditSnapshot.RecurrenceCount),
                nameof(IncidentAuditSnapshot.ResolutionCategory),
                nameof(IncidentAuditSnapshot.HasResolutionNote),
                nameof(IncidentAuditSnapshot.OpenedAt),
                nameof(IncidentAuditSnapshot.RecoveryStartedAt),
                nameof(IncidentAuditSnapshot.ResolvedAt),
                nameof(IncidentAuditSnapshot.ClosedAt),
                nameof(IncidentAuditSnapshot.RecoveryDurationMs),
                nameof(IncidentAuditSnapshot.OutageDurationMs),
                nameof(IncidentAuditSnapshot.Version));
    }
}
