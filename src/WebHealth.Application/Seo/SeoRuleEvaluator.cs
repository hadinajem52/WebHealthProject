using WebHealth.Application.Monitoring;
using WebHealth.Domain.Seo;

namespace WebHealth.Application.Seo;

/// <summary>
/// BR-E05 and BR-E09 are the same question with the answer reversed, so they are one setting: two
/// independent flags could contradict each other, and nothing would say which one won.
/// </summary>
public static class SeoIndexingExpectations
{
    /// <summary>Resolved from the environment: production must be indexable, non-production must not.</summary>
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

/// <summary>
/// Rule keys for the SEO configuration rules. Separate keys mean separate issue keys, so a page
/// with a wrong canonical and a page that is unreachable track and resolve as independent
/// incidents (BR-I04) without any new plumbing.
/// </summary>
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

/// <summary>
/// The policy the endpoint carries, resolved at finalization. It is deliberately not part of the
/// fingerprinted check snapshot — see docs/phase-6/SEO_Canonical_And_Indexing_Policy.md.
/// </summary>
public sealed record SeoPolicy(
    string ExpectedCanonicalHost,
    string IndexingExpectation,
    bool DescriptionRequired,
    bool IsProduction,
    string RobotsUserAgent = SeoPolicy.DefaultRobotsUserAgent)
{
    /// <summary>
    /// Only a fallback. The real value is the user agent the transport actually sends, because a
    /// robots group is selected by matching that string — a group naming the configured agent must
    /// be the group that applies.
    /// </summary>
    public const string DefaultRobotsUserAgent = "webhealthmonitor";

    public string ResolvedExpectation => SeoIndexingExpectations.Resolve(IndexingExpectation, IsProduction);

    /// <summary>
    /// An unmet expectation on production is High; everywhere else it is a Warning. Nothing here is
    /// Critical: a misconfigured page is not an unreachable one, and reserving Critical for
    /// availability is what keeps the severity vocabulary worth reading.
    /// </summary>
    public string EnvironmentSeverity => IsProduction ? FindingSeverities.High : FindingSeverities.Warning;
}

public static class SeoRuleEvaluator
{
    private static readonly char[] DirectiveSeparators = [','];

    public static IReadOnlyList<NormalizedFinding> Evaluate(SeoExtraction extraction, SeoPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(policy);

        // BR-E01: a non-applicable observation carries no facts to judge. Its recorded reason is
        // the answer, not a finding.
        return extraction.IsApplicable ? [.. Rules(extraction, policy)] : [];
    }

    private static IEnumerable<NormalizedFinding> Rules(SeoExtraction extraction, SeoPolicy policy)
    {
        // A body that hit the response cap may simply not contain the part that would have carried
        // the value, so concluding "missing" from it would be a guess (6.2 section 3.2).
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

    /// <summary>
    /// BR-E04 governs canonicals that exist — absolute, valid, unique, expected host. It does not
    /// require one, and raising a finding for every page without a canonical would fire across most
    /// of a healthy site and teach operators to ignore SEO findings altogether.
    /// </summary>
    private static IEnumerable<NormalizedFinding> CanonicalRules(SeoExtraction extraction, SeoPolicy policy)
    {
        if (extraction.CanonicalHref.Value is not { } authored)
        {
            // A canonical element with an empty or whitespace href is not "no canonical": the page
            // states a canonical and states nothing usable, which is the invalid case BR-E04 is
            // about. Only a page with no canonical element at all is silent.
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

        if (!Uri.TryCreate(authored, UriKind.Absolute, out _))
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

    /// <summary>
    /// The content is a comma-separated directive list, so it is read as tokens. A substring search
    /// would match "noindexing"; it would also have to special-case "none", which is the shorthand
    /// for "noindex, nofollow" and is the strongest directive a page can carry.
    /// </summary>
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
