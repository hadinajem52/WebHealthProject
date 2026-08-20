using Microsoft.EntityFrameworkCore;
using WebHealth.Application.Registry;
using WebHealth.Application.Seo;
using WebHealth.Domain.Seo;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.Seo;

/// <summary>
/// AC-07's read surface. Every filter is a predicate the database applies, and the visibility scope
/// is applied first — a view that fetched broadly and trimmed afterwards would already have read
/// rows the requester is not entitled to, which is a disclosure whether or not they are rendered.
/// </summary>
internal sealed class SeoReader(
    ApplicationDbContext dbContext,
    RegistryVisibility visibility,
    TimeProvider timeProvider) : ISeoReader
{
    /// <summary>
    /// Every SEO rule key is namespaced, so "does this page have SEO problems" is answered from the
    /// findings the rules already produced rather than by re-deriving the rules here. Re-deriving
    /// would let this list and the incident it links to disagree about the same page.
    /// </summary>
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

        // One row per endpoint: the observation with no newer sibling. An SEO view listing every
        // historical observation would show the same page a hundred times and bury the current
        // state, which is the only state anyone acts on.
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
                // The rule keys themselves, not a total: §11.2 asks this report for robots and
                // sitemap findings by name, and every SEO rule shares the "Seo." prefix, so a
                // count cannot tell a blocked origin from a missing meta description.
                observation.LogicalCheck.Result!.Findings
                    .Where(finding => finding.RuleKey.StartsWith(SeoRuleKeyPrefix))
                    .Select(finding => finding.RuleKey)
                    .ToList(),
                observation.ObservedAt))
            .ToArrayAsync(cancellationToken);

        return new(items, boundedPage, SeoQuery.PageSize, totalCount);
    }
}
