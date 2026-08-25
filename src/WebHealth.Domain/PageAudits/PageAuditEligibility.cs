using System.Net;
using WebHealth.Domain.Monitoring;
using WebHealth.Domain.Normalization;

namespace WebHealth.Domain.PageAudits;

public static class PageAuditIneligibilityReasons
{
    public const string UrlNotAbsolute = "UrlNotAbsolute";
    public const string SchemeNotSupported = "SchemeNotSupported";

    public const string UrlCarriesCredentials = "UrlCarriesCredentials";

    public const string UrlCarriesQuery = "UrlCarriesQuery";

    public const string AddressNotPublic = "AddressNotPublic";

    public const string HostNotPublic = "HostNotPublic";

    public static bool IsSupported(string value) =>
        value is UrlNotAbsolute or SchemeNotSupported or UrlCarriesCredentials
            or UrlCarriesQuery or AddressNotPublic or HostNotPublic;
}

public sealed record PageAuditEligibilityResult(bool IsEligible, string? Reason)
{
    public static PageAuditEligibilityResult Eligible { get; } = new(true, null);

    public static PageAuditEligibilityResult Rejected(string reason) => new(false, reason);
}

public static class PageAuditEligibility
{
    private static readonly string[] InternalSuffixes =
    [
        ".local",
        ".localhost",
        ".internal",
        ".intranet",
        ".private",
        ".corp",
        ".home",
        ".lan",
        ".home.arpa",
        ".test",
        ".invalid",
        ".example"
    ];

    public static PageAuditEligibilityResult Evaluate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.UrlNotAbsolute);
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            if (parsed.Scheme == Uri.UriSchemeFile
                && !url.TrimStart().StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.UrlNotAbsolute);
            }

            return PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.SchemeNotSupported);
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            return PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.UrlCarriesCredentials);
        }

        if (parsed.Query.Length > 0)
        {
            return PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.UrlCarriesQuery);
        }

        return EvaluateHost(parsed);
    }

    private static PageAuditEligibilityResult EvaluateHost(Uri parsed)
    {
        if (!UrlTextNormalization.HasReadableHost(parsed))
        {
            return PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.HostNotPublic);
        }

        var host = parsed.IdnHost.TrimEnd('.').ToLowerInvariant();

        if (IPAddress.TryParse(parsed.Host.Trim('[', ']'), out var literal))
        {
            return DestinationAddressPolicy.IsAllowed(literal)
                ? PageAuditEligibilityResult.Eligible
                : PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.AddressNotPublic);
        }

        if (host is "localhost")
        {
            return PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.HostNotPublic);
        }

        if (!host.Contains('.'))
        {
            return PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.HostNotPublic);
        }

        return InternalSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal))
            ? PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.HostNotPublic)
            : PageAuditEligibilityResult.Eligible;
    }
}
