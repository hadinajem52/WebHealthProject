using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class OwnerSubjectNames(ApplicationDbContext dbContext)
{
    private readonly Dictionary<Guid, string> names = [];
    private readonly HashSet<Guid> resolved = [];

    public async Task<Dictionary<Guid, string>> LoadAsync(
        IEnumerable<Guid> ownerSubjectIds,
        CancellationToken cancellationToken)
    {
        var ids = ownerSubjectIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var unresolved = Array.FindAll(ids, id => !resolved.Contains(id));
        if (unresolved.Length > 0)
        {
            var found = await dbContext.OwnerSubjects.AsNoTracking()
                .Where(owner => unresolved.Contains(owner.Id))
                .Select(owner => new
                {
                    owner.Id,
                    UserName = dbContext.Users.AsNoTracking()
                        .Where(user => user.Id == owner.UserId)
                        .Select(user => user.DisplayName)
                        .FirstOrDefault(),
                    TeamName = dbContext.Teams.AsNoTracking()
                        .Where(team => team.Id == owner.TeamId)
                        .Select(team => team.Name)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            foreach (var owner in found)
            {
                if ((owner.UserName ?? owner.TeamName) is { } name)
                {
                    names[owner.Id] = name;
                }
            }

            resolved.UnionWith(unresolved);
        }

        var result = new Dictionary<Guid, string>(ids.Length);
        foreach (var id in ids)
        {
            if (names.TryGetValue(id, out var name))
            {
                result[id] = name;
            }
        }

        return result;
    }
}
