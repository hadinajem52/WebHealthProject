using System.Text;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Seo;

namespace WebHealth.Web.Models;

/// <summary>
/// Renders an issue key as something a reader can act on.
/// <para>
/// The key itself — <c>v1|HttpAvailability|Seo.RobotsBlocksSite|default</c> — is a stable
/// identifier, not a sentence. It carries a schema version, the monitor type already shown in its
/// own column, and a discriminator that is the literal word "default" for every availability rule
/// and a 64-character certificate fingerprint for the certificate one. Printing it whole asks the
/// reader to parse four fields to learn one thing.
/// </para>
/// <para>
/// The key stays the deduplication identity and is still shown verbatim on the incident detail
/// page, so nothing here changes what an operator can look up — only what the list leads with.
/// </para>
/// </summary>
public static class IssueDisplay
{
    private const int ExpectedSegments = 4;
    private const int RuleSegment = 2;

    /// <summary>
    /// Written from the reader's side — what is wrong with the site, not which predicate fired.
    /// A rule absent from this map is described from its own name rather than falling back to the
    /// raw key, so a rule added later reads as a phrase on the day it ships.
    /// </summary>
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        // SEO configuration (BR-E02 to BR-E05, BR-E09)
        [RobotsRules.BlocksSite] = "robots.txt blocks the whole site",
        [RobotsRules.BlocksEndpoint] = "robots.txt blocks this page",
        [RobotsRules.Unavailable] = "robots.txt could not be read",
        [RobotsRules.SitemapMissing] = "Sitemap is missing",
        [SeoRules.TitleMissing] = "Title is missing",
        [SeoRules.TitleDuplicate] = "Page has more than one title",
        [SeoRules.DescriptionMissing] = "Meta description is missing",
        [SeoRules.CanonicalNotAbsolute] = "Canonical URL is not absolute",
        [SeoRules.CanonicalInvalid] = "Canonical URL is not a valid URL",
        [SeoRules.CanonicalDuplicate] = "Page has more than one canonical URL",
        [SeoRules.CanonicalUnexpectedHost] = "Canonical URL points at another host",
        [SeoRules.NoIndexUnexpected] = "Page is set to noindex but should be indexable",
        [SeoRules.IndexableUnexpected] = "Page is indexable but should not be",

        // Availability and performance
        ["Http.Dns"] = "Hostname did not resolve",
        ["Http.Connection"] = "Connection refused or unreachable",
        ["Http.Tls"] = "TLS negotiation failed",
        ["Http.Timeout"] = "Request timed out",
        ["Http.Cancellation"] = "Check was cancelled",
        ["Http.ClientError"] = "Responded 4xx",
        ["Http.ServerError"] = "Responded 5xx",
        ["Http.RedirectLoop"] = "Redirects formed a loop",
        ["Http.ExcessiveRedirects"] = "Too many redirects",
        ["Http.ContentMismatch"] = "Required content was not found on the page",
        ["Http.ResponseTooLarge"] = "Response exceeded the size limit",
        ["Http.HttpsRequired"] = "HTTPS is required but the endpoint served HTTP",
        ["Http.InvalidConfiguration"] = "Monitor configuration is invalid",
        ["Http.DestinationPolicy"] = "Destination is blocked by network policy",
        ["Http.InvalidRedirect"] = "Redirect target was rejected",
        ["Http.ExecutionExhausted"] = "Check could not be completed",
        ["Http.TargetIneligible"] = "Target is not eligible to be checked",
        ["Http.Protocol"] = "Protocol error",
        ["Http.Unknown"] = "Failed for an unrecognised reason",
        ["Http.SlowResponse"] = "Slower than its response-time threshold",
        ["Http.PageTooLarge"] = "Larger than its page-size threshold",

        // Certificates
        [SslMonitorIdentity.ExpiryRuleKey] = "Certificate is expiring",
    };

    /// <summary>The issue in words. Falls back to the whole key if it is not in the known shape.</summary>
    public static string Describe(string? issueKey)
    {
        if (string.IsNullOrWhiteSpace(issueKey)) return string.Empty;

        var segments = issueKey.Split('|');
        if (segments.Length != ExpectedSegments) return issueKey;

        return DescribeRule(segments[RuleSegment]);
    }

    /// <summary>
    /// The same wording from a bare rule key, for surfaces that hold the rule without the key
    /// around it. Both entry points share one map so a finding and the incident it opens cannot
    /// describe the same rule differently.
    /// </summary>
    public static string DescribeRule(string? ruleKey)
    {
        if (string.IsNullOrWhiteSpace(ruleKey)) return string.Empty;
        return Descriptions.TryGetValue(ruleKey, out var described) ? described : Humanize(ruleKey);
    }

    /// <summary>
    /// Turns <c>Seo.SomeNewRule</c> into "Some new rule". The prefix is dropped because the monitor
    /// type is its own column, and only the first word is capitalised so the result reads as a
    /// phrase rather than a label.
    /// </summary>
    private static string Humanize(string rule)
    {
        var name = rule[(rule.IndexOf('.') + 1)..];
        if (name.Length == 0) return rule;

        var text = new StringBuilder(name.Length + 8);
        text.Append(name[0]);
        for (var index = 1; index < name.Length; index++)
        {
            if (char.IsUpper(name[index]) && !char.IsUpper(name[index - 1])) text.Append(' ');
            text.Append(char.IsUpper(name[index]) ? char.ToLowerInvariant(name[index]) : name[index]);
        }

        return text.ToString();
    }
}
