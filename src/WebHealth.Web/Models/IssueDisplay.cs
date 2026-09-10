using System.Text;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Seo;

namespace WebHealth.Web.Models;

public static class IssueDisplay
{
    private const int ExpectedSegments = 4;
    private const int RuleSegment = 2;

    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
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

        [SslMonitorIdentity.ExpiryRuleKey] = "Certificate is expiring",
        ["PageAudit.Performance.Score"] = "Performance score fell below its threshold",
        ["PageAudit.Accessibility.Score"] = "Accessibility score fell below its threshold",
        ["PageAudit.BestPractices.Score"] = "Best Practices score fell below its threshold",
        ["PageAudit.Seo.Score"] = "SEO score fell below its threshold",
        ["PageAudit.Performance.FirstContentfulPaint"] = "First Contentful Paint exceeded its threshold",
        ["PageAudit.Performance.LargestContentfulPaint"] = "Largest Contentful Paint exceeded its threshold",
        ["PageAudit.Performance.TotalBlockingTime"] = "Total Blocking Time exceeded its threshold",
        ["PageAudit.Performance.CumulativeLayoutShift"] = "Cumulative Layout Shift exceeded its threshold",
        ["PageAudit.Performance.SpeedIndex"] = "Speed Index exceeded its threshold"
    };

    public static string Describe(string? issueKey)
    {
        if (string.IsNullOrWhiteSpace(issueKey)) return string.Empty;

        var segments = issueKey.Split('|');
        if (segments.Length != ExpectedSegments) return issueKey;

        return DescribeRule(segments[RuleSegment]);
    }

    public static string DescribeRule(string? ruleKey)
    {
        if (string.IsNullOrWhiteSpace(ruleKey)) return string.Empty;
        return Descriptions.TryGetValue(ruleKey, out var described) ? described : Humanize(ruleKey);
    }

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
