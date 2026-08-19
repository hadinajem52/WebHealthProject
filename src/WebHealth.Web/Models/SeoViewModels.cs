using WebHealth.Application.Seo;
using WebHealth.Domain.Seo;

namespace WebHealth.Web.Models;

/// <summary>
/// The SEO list. The filter values are echoed back so the form keeps its state, but they are only
/// ever *displayed* here — the reader applied them in the database, and nothing on this page
/// re-filters what it was handed.
/// </summary>
public sealed record SeoListViewModel(
    SeoListPage Results,
    string? Applicability,
    string? Environment,
    bool ProblemsOnly,
    string FilterSummary)
{
    public static string Describe(string? applicability, string? environment, bool problemsOnly)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(applicability)) parts.Add(applicability);
        if (environment == SeoQuery.Production) parts.Add("production only");
        if (environment == SeoQuery.NonProduction) parts.Add("non-production only");
        if (problemsOnly) parts.Add("with SEO findings");
        return parts.Count == 0 ? "All endpoints" : string.Join(", ", parts);
    }

    /// <summary>
    /// What the environment expects of this page, resolved the same way the rules resolve it, so
    /// the column and the finding cannot disagree.
    /// </summary>
    public static string DescribeExpectation(SeoListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return SeoIndexingExpectations.Resolve(item.PolicyIndexingExpectation, item.IsProduction)
            == SeoIndexingExpectations.Indexable
            ? "Indexable"
            : "Not indexed";
    }

    /// <summary>
    /// A value that was never extracted and a value that is genuinely empty look the same on a
    /// page unless one of them says so.
    /// </summary>
    public static string Present(string? value, int length) =>
        string.IsNullOrEmpty(value) ? "—" : $"{value} ({length} chars)";
}
