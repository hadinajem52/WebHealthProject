using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Registry;
using WebHealth.Application.Seo;
using WebHealth.Domain.Seo;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Seo;

internal sealed class SeoReader(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility,
    TimeProvider timeProvider) : ISeoReader
{
    private const string SeoRuleKeyPrefix = "Seo.";

    public async Task<SeoListPage> ListAsync(
        SeoQuery query,
        RegistryAccessContext access,
        int page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(access);
        var now = timeProvider.GetUtcNow();

        var endpoints = visibility.ApplyEndpointScope(
            dbContext.Endpoints.AsNoTracking().Where(endpoint => endpoint.DeletedAt == null),
            access,
            now);

        if (query.WebsiteId is { } websiteId)
        {
            endpoints = endpoints.Where(endpoint => endpoint.Environment.WebsiteId == websiteId);
        }

        endpoints = query.Environment switch
        {
            SeoQuery.Production => endpoints.Where(endpoint => endpoint.Environment.IsProduction),
            SeoQuery.NonProduction => endpoints.Where(endpoint => !endpoint.Environment.IsProduction),
            _ => endpoints
        };

        var visibleEndpointIds = endpoints.Select(endpoint => endpoint.Id);
        var observations = dbContext.SeoObservations.AsNoTracking()
            .Where(observation => visibleEndpointIds.Contains(observation.EndpointMonitor.EndpointId));

        var latest = observations.Where(observation => !observations.Any(newer =>
            newer.EndpointMonitor.EndpointId == observation.EndpointMonitor.EndpointId
            && (newer.ObservedAt > observation.ObservedAt
                || (newer.ObservedAt == observation.ObservedAt
                    && newer.LogicalCheckId > observation.LogicalCheckId))));

        if (!string.IsNullOrWhiteSpace(query.Applicability))
        {
            latest = latest.Where(observation => observation.Applicability == query.Applicability);
        }

        if (query.ProblemsOnly)
        {
            latest = latest.Where(observation =>
                observation.LogicalCheck.Result!.Findings.Any(finding =>
                    finding.RuleKey.StartsWith(SeoRuleKeyPrefix)));
        }

        if (SeoFindingGroups.IsSelectable(query.Subject))
        {
            var subjectRuleKeys = SeoFindingGroups.RuleKeysFor(query.Subject);
            latest = latest.Where(observation =>
                observation.LogicalCheck.Result!.Findings.Any(finding =>
                    subjectRuleKeys.Contains(finding.RuleKey)));
        }

        var totalCount = await latest.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)SeoQuery.PageSize));
        var boundedPage = Math.Clamp(page, 1, totalPages);

        var items = await latest
            .OrderBy(observation => observation.EndpointMonitor.Endpoint.DisplayUrl)
            .ThenBy(observation => observation.LogicalCheckId)
            .Skip((boundedPage - 1) * SeoQuery.PageSize)
            .Take(SeoQuery.PageSize)
            .Select(observation => new SeoListItem(
                observation.EndpointMonitor.EndpointId,
                observation.LogicalCheckId,
                observation.EndpointMonitor.Endpoint.DisplayUrl,
                observation.EndpointMonitor.Endpoint.Environment.Website.Name,
                observation.EndpointMonitor.Endpoint.Environment.Name,
                observation.EndpointMonitor.Endpoint.Environment.IsProduction,
                observation.Applicability,
                observation.NotApplicableReason,
                observation.DocumentTruncated,
                observation.Title,
                observation.TitleLength,
                observation.TitleCount,
                observation.MetaDescription,
                observation.MetaDescriptionLength,
                observation.CanonicalAbsoluteUrl,
                observation.CanonicalCount,
                observation.RobotsMeta,
                observation.PolicyIndexingExpectation ?? SeoIndexingExpectations.Default,
                observation.LogicalCheck.Result!.Findings
                    .Where(finding => finding.RuleKey.StartsWith(SeoRuleKeyPrefix))
                    .Select(finding => finding.RuleKey)
                    .ToList(),
                observation.ObservedAt))
            .ToArrayAsync(cancellationToken);

        return new(items, boundedPage, SeoQuery.PageSize, totalCount);
    }
}
