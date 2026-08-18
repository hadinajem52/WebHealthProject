using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

/// <summary>
/// Resolves owner subjects to the name a person reads. An owner subject is either a user or a
/// team, so the display name comes from one of two tables; every read surface that shows an
/// owner resolves it the same way from here rather than each writing its own join.
/// </summary>
internal sealed class OwnerSubjectNames(ApplicationDbContext dbContext)
{
    public async Task<Dictionary<Guid, string>> LoadAsync(
        IEnumerable<Guid> ownerSubjectIds,
        CancellationToken cancellationToken)
    {
        var ids = ownerSubjectIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var users = await dbContext.OwnerSubjects.AsNoTracking()
            .Where(owner => ids.Contains(owner.Id) && owner.UserId != null)
            .Join(dbContext.Users.AsNoTracking(), owner => owner.UserId, user => user.Id,
                (owner, user) => new { owner.Id, Name = user.DisplayName })
            .ToListAsync(cancellationToken);
        var teams = await dbContext.OwnerSubjects.AsNoTracking()
            .Where(owner => ids.Contains(owner.Id) && owner.TeamId != null)
            .Join(dbContext.Teams.AsNoTracking(), owner => owner.TeamId, team => team.Id,
                (owner, team) => new { owner.Id, team.Name })
            .ToListAsync(cancellationToken);
        return users.Concat(teams).ToDictionary(owner => owner.Id, owner => owner.Name);
    }
}
