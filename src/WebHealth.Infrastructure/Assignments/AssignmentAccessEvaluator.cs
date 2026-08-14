using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Assignments;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Assignments;

public sealed class AssignmentAccessEvaluator(ApplicationDbContext dbContext)
    : IAssignmentAccessEvaluator
{
    public Task<bool> IsAssignedAsync(
        Guid userId,
        Guid ownerSubjectId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default) =>
        dbContext.OwnerSubjects.AsNoTracking()
            .Where(subject => subject.Id == ownerSubjectId)
            .AnyAsync(subject => dbContext.Users.Any(user =>
                    user.Id == userId
                    && !user.IsDisabled
                    && subject.UserId == user.Id)
                || dbContext.TeamMembers.Any(member =>
                    member.TeamId == subject.TeamId
                    && member.UserId == userId
                    && !member.Team.IsDisabled
                    && !member.User.IsDisabled
                    && member.EffectiveFrom <= at
                    && (member.EffectiveUntil == null || member.EffectiveUntil > at)),
                cancellationToken);
}
