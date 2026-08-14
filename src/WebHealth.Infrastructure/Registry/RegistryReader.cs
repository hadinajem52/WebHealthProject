using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class RegistryReader(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility) : IRegistryReader
{
    public async Task<IReadOnlyList<ClientListItem>> ListClientsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default) =>
        await ListClientsAsync(access, isDeleted: false, cancellationToken);

    public async Task<IReadOnlyList<ClientListItem>> ListDeletedClientsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return [];
        }

        return await ListClientsAsync(access, isDeleted: true, cancellationToken);
    }

    private async Task<IReadOnlyList<ClientListItem>> ListClientsAsync(
        RegistryAccessContext access,
        bool isDeleted,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var clients = await visibility.ApplyClientScope(dbContext.Clients.AsNoTracking(), access, now)
            .Where(client => (client.DeletedAt != null) == isDeleted)
            .OrderBy(client => client.Name)
            .Select(client => new
            {
                client.Id,
                client.Name,
                client.OwnerSubjectId,
                client.IsActive,
                IsDeleted = client.DeletedAt != null,
                client.Version
            })
            .ToListAsync(cancellationToken);
        var clientIds = clients.Select(client => client.Id).ToArray();
        var websiteCounts = await visibility.ApplyWebsiteScope(dbContext.Websites.AsNoTracking(), access, now)
            .Where(website => website.DeletedAt == null && clientIds.Contains(website.ClientId))
            .GroupBy(website => website.ClientId)
            .Select(group => new { ClientId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.ClientId, row => row.Count, cancellationToken);
        var ownerNames = await LoadOwnerNamesAsync(
            clients.Select(client => client.OwnerSubjectId),
            cancellationToken);

        return clients.Select(client => new ClientListItem(
            client.Id,
            client.Name,
            ownerNames[client.OwnerSubjectId],
            client.IsActive,
            client.IsDeleted,
            client.Version,
            websiteCounts.GetValueOrDefault(client.Id))).ToArray();
    }

    public async Task<ClientDetails?> FindClientAsync(
        Guid clientId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var canManage = RegistryVisibility.CanManage(access);
        var client = await visibility.ApplyClientScope(dbContext.Clients.AsNoTracking(), access, now)
            .Where(candidate => candidate.Id == clientId)
            .Where(candidate => canManage || candidate.DeletedAt == null)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Name,
                candidate.OwnerSubjectId,
                candidate.Notes,
                candidate.IsActive,
                IsDeleted = candidate.DeletedAt != null,
                candidate.Version
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (client is null)
        {
            return null;
        }

        var websites = await LoadWebsiteRowsAsync(
            visibility.ApplyWebsiteScope(dbContext.Websites.AsNoTracking(), access, now)
                .Where(website => website.ClientId == clientId && website.DeletedAt == null),
            cancellationToken);
        var ownerIds = websites.Select(website => website.OwnerSubjectId)
            .Append(client.OwnerSubjectId);
        var ownerNames = await LoadOwnerNamesAsync(ownerIds, cancellationToken);
        var tags = await LoadWebsiteTagsAsync(websites.Select(website => website.Id), cancellationToken);
        return new ClientDetails(
            client.Id,
            client.Name,
            client.OwnerSubjectId,
            ownerNames[client.OwnerSubjectId],
            client.Notes,
            client.IsActive,
            client.IsDeleted,
            client.Version,
            ToWebsiteItems(websites, ownerNames, tags));
    }

    public async Task<IReadOnlyList<WebsiteListItem>> ListWebsitesAsync(
        RegistryAccessContext access,
        Guid? tagId = null,
        CancellationToken cancellationToken = default) =>
        await ListWebsitesAsync(access, isDeleted: false, tagId, cancellationToken);

    public async Task<IReadOnlyList<RegistryTagOption>> ListTagsAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var visibleWebsiteIds = await visibility.ApplyWebsiteScope(
                dbContext.Websites.AsNoTracking(), access, DateTimeOffset.UtcNow)
            .Where(website => website.DeletedAt == null)
            .Select(website => website.Id)
            .ToArrayAsync(cancellationToken);
        if (visibleWebsiteIds.Length == 0)
        {
            return [];
        }

        var tagRows = await dbContext.WebsiteTags.AsNoTracking()
            .Where(websiteTag => visibleWebsiteIds.Contains(websiteTag.WebsiteId))
            .Select(websiteTag => new { websiteTag.TagId, websiteTag.Tag.Name })
            .ToListAsync(cancellationToken);
        return tagRows.GroupBy(row => new { row.TagId, row.Name })
            .Select(group => new RegistryTagOption(group.Key.TagId, group.Key.Name, group.Count()))
            .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<WebsiteListItem>> ListDeletedWebsitesAsync(
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        if (!RegistryVisibility.CanManage(access))
        {
            return [];
        }

        return await ListWebsitesAsync(access, isDeleted: true, null, cancellationToken);
    }

    private async Task<IReadOnlyList<WebsiteListItem>> ListWebsitesAsync(
        RegistryAccessContext access,
        bool isDeleted,
        Guid? tagId,
        CancellationToken cancellationToken)
    {
        var query = visibility.ApplyWebsiteScope(
                dbContext.Websites.AsNoTracking(),
                access,
                DateTimeOffset.UtcNow)
            .Where(website => (website.DeletedAt != null) == isDeleted);
        if (tagId is { } selectedTagId)
        {
            query = query.Where(website => website.WebsiteTags.Any(websiteTag => websiteTag.TagId == selectedTagId));
        }

        var websites = await LoadWebsiteRowsAsync(
            query,
            cancellationToken);
        var ownerNames = await LoadOwnerNamesAsync(
            websites.Select(website => website.OwnerSubjectId),
            cancellationToken);
        var tags = await LoadWebsiteTagsAsync(websites.Select(website => website.Id), cancellationToken);
        return ToWebsiteItems(websites, ownerNames, tags);
    }

    public async Task<WebsiteDetails?> FindWebsiteAsync(
        Guid websiteId,
        RegistryAccessContext access,
        CancellationToken cancellationToken = default)
    {
        var canManage = RegistryVisibility.CanManage(access);
        var websites = await LoadWebsiteRowsAsync(
            visibility.ApplyWebsiteScope(
                    dbContext.Websites.AsNoTracking(),
                access,
                DateTimeOffset.UtcNow)
                .Where(website => website.Id == websiteId)
                .Where(website => canManage || website.DeletedAt == null),
            cancellationToken);
        var website = websites.SingleOrDefault();
        if (website is null)
        {
            return null;
        }

        var ownerNames = await LoadOwnerNamesAsync([website.OwnerSubjectId], cancellationToken);
        var tags = await LoadWebsiteTagsAsync([website.Id], cancellationToken);
        return new WebsiteDetails(
            website.Id,
            website.ClientId,
            website.ClientName,
            website.Name,
            website.OwnerSubjectId,
            ownerNames[website.OwnerSubjectId],
            website.TechnologyCms,
            website.IsEnabled,
            website.IsDeleted,
            website.Version,
            website.ActiveEnvironmentCount,
            tags.GetValueOrDefault(website.Id, []));
    }

    public async Task<IReadOnlyList<RegistryOwnerOption>> ListOwnersAsync(
        Guid? includeOwnerSubjectId = null,
        CancellationToken cancellationToken = default)
    {
        var users = await dbContext.OwnerSubjects.AsNoTracking()
            .Where(subject => subject.UserId != null)
            .Join(
                dbContext.Users.AsNoTracking(),
                subject => subject.UserId,
                user => user.Id,
                (subject, user) => new { Subject = subject, User = user })
            .Where(row => !row.User.IsDisabled || row.Subject.Id == includeOwnerSubjectId)
            .Select(row => new RegistryOwnerOption(
                row.Subject.Id,
                row.User.DisplayName,
                "User"))
            .ToListAsync(cancellationToken);
        var teams = await dbContext.OwnerSubjects.AsNoTracking()
            .Where(subject => subject.TeamId != null)
            .Join(
                dbContext.Teams.AsNoTracking(),
                subject => subject.TeamId,
                team => team.Id,
                (subject, team) => new { Subject = subject, Team = team })
            .Where(row => !row.Team.IsDisabled || row.Subject.Id == includeOwnerSubjectId)
            .Select(row => new RegistryOwnerOption(
                row.Subject.Id,
                row.Team.Name,
                "Team"))
            .ToListAsync(cancellationToken);

        return users.Concat(teams)
            .OrderBy(owner => owner.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(owner => owner.OwnerType, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<Dictionary<Guid, string>> LoadOwnerNamesAsync(
        IEnumerable<Guid> ownerSubjectIds,
        CancellationToken cancellationToken)
    {
        var ids = ownerSubjectIds.Distinct().ToArray();
        var userOwners = await dbContext.OwnerSubjects.AsNoTracking()
            .Where(subject => ids.Contains(subject.Id) && subject.UserId != null)
            .Join(
                dbContext.Users.AsNoTracking(),
                subject => subject.UserId,
                user => user.Id,
                (subject, user) => new { subject.Id, Name = user.DisplayName })
            .ToListAsync(cancellationToken);
        var teamOwners = await dbContext.OwnerSubjects.AsNoTracking()
            .Where(subject => ids.Contains(subject.Id) && subject.TeamId != null)
            .Join(
                dbContext.Teams.AsNoTracking(),
                subject => subject.TeamId,
                team => team.Id,
                (subject, team) => new { subject.Id, team.Name })
            .ToListAsync(cancellationToken);
        return userOwners.Concat(teamOwners).ToDictionary(owner => owner.Id, owner => owner.Name);
    }

    private static IReadOnlyList<WebsiteListItem> ToWebsiteItems(
        IEnumerable<WebsiteRow> websites,
        IReadOnlyDictionary<Guid, string> ownerNames,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? tags = null) =>
        websites.Select(website => new WebsiteListItem(
            website.Id,
            website.ClientId,
            website.ClientName,
            website.Name,
            ownerNames[website.OwnerSubjectId],
            website.TechnologyCms,
            website.IsEnabled,
            website.IsDeleted,
            website.Version,
            website.ActiveEnvironmentCount,
            tags?.GetValueOrDefault(website.Id, []) ?? [])).ToArray();

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> LoadWebsiteTagsAsync(
        IEnumerable<Guid> websiteIds,
        CancellationToken cancellationToken)
    {
        var ids = websiteIds.Distinct().ToArray();
        var rows = await dbContext.WebsiteTags.AsNoTracking()
            .Where(websiteTag => ids.Contains(websiteTag.WebsiteId))
            .OrderBy(websiteTag => websiteTag.Tag.Name)
            .Select(websiteTag => new { websiteTag.WebsiteId, websiteTag.Tag.Name })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.WebsiteId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(row => row.Name).ToArray());
    }

    private static Task<List<WebsiteRow>> LoadWebsiteRowsAsync(
        IQueryable<Website> websites,
        CancellationToken cancellationToken) =>
        websites.OrderBy(website => website.Client.Name)
            .ThenBy(website => website.Name)
            .Select(website => new WebsiteRow(
                website.Id,
                website.ClientId,
                website.Client.Name,
                website.Name,
                website.OwnerSubjectId,
                website.TechnologyCms,
                website.IsEnabled,
                website.DeletedAt != null,
                website.Version,
                website.Environments.Count(environment =>
                    environment.DeletedAt == null && environment.IsActive)))
            .ToListAsync(cancellationToken);

    private sealed record WebsiteRow(
        Guid Id,
        Guid ClientId,
        string ClientName,
        string Name,
        Guid OwnerSubjectId,
        string? TechnologyCms,
        bool IsEnabled,
        bool IsDeleted,
        long Version,
        int ActiveEnvironmentCount);
}
