using WebHealth.Application.Monitoring;
using WebHealth.Domain.Seo;

namespace WebHealth.Application.Seo;

public static class SeoIndexingExpectations
{
    public const string Default = "Default";
    public const string Indexable = "Indexable";
    public const string NoIndex = "NoIndex";

    public static bool IsSupported(string value) => value is Default or Indexable or NoIndex;

    public static string Resolve(string configured, bool isProduction) => configured switch
    {
        Indexable or NoIndex => configured,
        _ => isProduction ? Indexable : NoIndex
    };
}

public static class SeoRules
{
    public const string TitleMissing = "Seo.TitleMissing";
    public const string TitleDuplicate = "Seo.TitleDuplicate";
    public const string DescriptionMissing = "Seo.DescriptionMissing";
    public const string CanonicalNotAbsolute = "Seo.CanonicalNotAbsolute";
    public const string CanonicalInvalid = "Seo.CanonicalInvalid";
    public const string CanonicalDuplicate = "Seo.CanonicalDuplicate";
    public const string CanonicalUnexpectedHost = "Seo.CanonicalUnexpectedHost";
    public const string NoIndexUnexpected = "Seo.NoIndexUnexpected";
    public const string IndexableUnexpected = "Seo.IndexableUnexpected";
}

public sealed record SeoPolicy(
    string ExpectedCanonicalHost,
    string IndexingExpectation,
    bool DescriptionRequired,
    bool IsProduction,
    string RobotsUserAgent = SeoPolicy.DefaultRobotsUserAgent)
{
    public const string DefaultRobotsUserAgent = "webhealthmonitor";

    public string ResolvedExpectation => SeoIndexingExpectations.Resolve(IndexingExpectation, IsProduction);

    public string EnvironmentSeverity => IsProduction ? FindingSeverities.High : FindingSeverities.Warning;
}

public static class SeoRuleEvaluator
{
    private static readonly char[] DirectiveSeparators = [','];

    public static IReadOnlyList<NormalizedFinding> Evaluate(SeoExtraction extraction, SeoPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(policy);

        return extraction.IsApplicable ? [.. Rules(extraction, policy)] : [];
    }

    private static IEnumerable<NormalizedFinding> Rules(SeoExtraction extraction, SeoPolicy policy)
    {
        var absenceIsEvidence = !extraction.DocumentTruncated;

        if (absenceIsEvidence && extraction.Title.Value is null)
        {
            yield return Finding(SeoRules.TitleMissing, SeoFailureCategories.Title,
                "No title element", "A non-empty title element", FindingSeverities.Warning);
        }

        if (extraction.TitleCount > 1)
        {
            yield return Finding(SeoRules.TitleDuplicate, SeoFailureCategories.Title,
                $"{extraction.TitleCount} title elements", "Exactly one title element",
                FindingSeverities.Warning);
        }

        if (absenceIsEvidence && policy.DescriptionRequired && extraction.MetaDescription.Value is null)
        {
            yield return Finding(SeoRules.DescriptionMissing, SeoFailureCategories.Description,
                "No meta description", "A non-empty meta description", FindingSeverities.Warning);
        }

        foreach (var finding in CanonicalRules(extraction, policy))
        {
            yield return finding;
        }

        foreach (var finding in IndexingRules(extraction, policy, absenceIsEvidence))
        {
            yield return finding;
        }
    }

    private static IEnumerable<NormalizedFinding> CanonicalRules(SeoExtraction extraction, SeoPolicy policy)
    {
        if (extraction.CanonicalHref.Value is not { } authored)
        {
            if (extraction.CanonicalCount > 0)
            {
                yield return Finding(SeoRules.CanonicalInvalid, SeoFailureCategories.Canonical,
                    "Empty canonical href", "An absolute http or https URL", FindingSeverities.Warning);
            }

            yield break;
        }

        if (extraction.CanonicalCount > 1)
        {
            yield return Finding(SeoRules.CanonicalDuplicate, SeoFailureCategories.Canonical,
                $"{extraction.CanonicalCount} canonical links", "Exactly one canonical link",
                FindingSeverities.Warning);
        }

        if (extraction.CanonicalAbsoluteUrl is not { } resolved)
        {
            yield return Finding(SeoRules.CanonicalInvalid, SeoFailureCategories.Canonical,
                authored, "An absolute http or https URL", FindingSeverities.Warning);
            yield break;
        }

        if (!Uri.TryCreate(authored, UriKind.Absolute, out var authoredUri)
            || authoredUri.Scheme != Uri.UriSchemeHttp
            && authoredUri.Scheme != Uri.UriSchemeHttps)
        {
            yield return Finding(SeoRules.CanonicalNotAbsolute, SeoFailureCategories.Canonical,
                authored, resolved, FindingSeverities.Warning);
        }

        var host = new Uri(resolved, UriKind.Absolute).Host;
        if (!string.Equals(host, policy.ExpectedCanonicalHost, StringComparison.OrdinalIgnoreCase))
        {
            yield return Finding(SeoRules.CanonicalUnexpectedHost, SeoFailureCategories.Canonical,
                host, policy.ExpectedCanonicalHost, policy.EnvironmentSeverity);
        }
    }

    private static IEnumerable<NormalizedFinding> IndexingRules(
        SeoExtraction extraction,
        SeoPolicy policy,
        bool absenceIsEvidence)
    {
        var isNoIndex = IsNoIndex(extraction.RobotsMeta.Value);
        var expectation = policy.ResolvedExpectation;

        if (expectation == SeoIndexingExpectations.Indexable && isNoIndex)
        {
            yield return Finding(SeoRules.NoIndexUnexpected, SeoFailureCategories.Indexing,
                extraction.RobotsMeta.Value, "An indexable page", policy.EnvironmentSeverity);
        }

        if (expectation == SeoIndexingExpectations.NoIndex && !isNoIndex && absenceIsEvidence)
        {
            yield return Finding(SeoRules.IndexableUnexpected, SeoFailureCategories.Indexing,
                extraction.RobotsMeta.Value ?? "No robots directive", "noindex",
                policy.EnvironmentSeverity);
        }
    }

    public static bool IsNoIndex(string? robotsMeta) =>
        robotsMeta is not null
        && robotsMeta.Split(DirectiveSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(token => token.Equals("noindex", StringComparison.OrdinalIgnoreCase)
                || token.Equals("none", StringComparison.OrdinalIgnoreCase));

    private static NormalizedFinding Finding(
        string ruleKey,
        string category,
        string? observed,
        string? expected,
        string severity) =>
        new(category, ruleKey, severity, FindingValues.Bound(observed), FindingValues.Bound(expected),
            HttpIssueIdentity.Create(ruleKey));
}
