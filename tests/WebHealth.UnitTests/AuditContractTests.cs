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
            nameof(IAuditTrailWriter.RecordTeamUpdatedAsync));
        methods.SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should().OnlyContain(type =>
                type == typeof(AuditWriteContext)
                || type == typeof(UserAuditSnapshot)
                || type == typeof(TeamAuditSnapshot)
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
    }
}
