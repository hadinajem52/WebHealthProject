using Microsoft.EntityFrameworkCore;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

/// <summary>
/// Resolves owner subjects to the name a person reads. An owner subject is either a user or a
/// team, so the display name comes from one of two tables; every read surface that shows an
/// owner resolves it the same way from here rather than each writing its own join.
/// </summary>
/// <remarks>
/// Registered per scope, and the resolutions it makes are remembered for that scope. A single
/// screen asks for owner names several times over — the rows, the incident list and the filter
/// options each resolve their own — and every one of those was a pair of round trips for names
/// that had already been read. Names cannot change inside one request, so the second ask is
/// answered from what the first already learned. Identifiers that resolved to nothing are
/// remembered too, otherwise an owner subject with neither a user nor a team would be looked up
/// again on every call.
/// </remarks>
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
            // Both sides are read in one statement. An owner subject is a user or a team and the
            // table refuses to be neither or both, so the two names are alternatives rather than
            // separate questions, and asking them separately made every owner lookup two round
            // trips to assemble one column.
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

            // Marked after the read, so a cancelled or failed lookup is retried rather than
            // remembered as an owner that has no name.
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
