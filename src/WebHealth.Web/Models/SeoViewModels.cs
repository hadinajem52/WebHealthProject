using WebHealth.Application.Seo;
using WebHealth.Domain.Seo;

namespace WebHealth.Web.Models;

public sealed record SeoListViewModel(
    SeoListPage Results,
    string? Applicability,
    string? Environment,
    bool ProblemsOnly,
    string? Subject,
    string FilterSummary)
{
    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Applicability)
        || !string.IsNullOrWhiteSpace(Environment)
        || !string.IsNullOrWhiteSpace(Subject)
        || ProblemsOnly;

    public static string Describe(
        string? applicability,
        string? environment,
        bool problemsOnly,
        string? subject = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(applicability)) parts.Add(applicability);
        if (environment == SeoQuery.Production) parts.Add("production only");
        if (environment == SeoQuery.NonProduction) parts.Add("non-production only");
        if (!string.IsNullOrWhiteSpace(subject)) parts.Add($"with {subject.ToLowerInvariant()} findings");
        else if (problemsOnly) parts.Add("with SEO findings");
        return parts.Count == 0 ? "All endpoints" : string.Join(", ", parts);
    }

    public static string DescribeExpectation(SeoListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return SeoIndexingExpectations.Resolve(item.PolicyIndexingExpectation, item.IsProduction)
            == SeoIndexingExpectations.Indexable
            ? "Indexable"
            : "Not indexed";
    }
}
