using System.Net;
using WebHealth.Domain.Monitoring;

namespace WebHealth.Domain.PageAudits;

/// <summary>Why a URL cannot be sent to a third-party auditor.</summary>
public static class PageAuditIneligibilityReasons
{
    public const string UrlNotAbsolute = "UrlNotAbsolute";
    public const string SchemeNotSupported = "SchemeNotSupported";

    /// <summary>Credentials in the URL would be handed to the provider along with it.</summary>
    public const string UrlCarriesCredentials = "UrlCarriesCredentials";

    /// <summary>
    /// The URL carries a query string. Normalization keeps queries, and a query is where a signed
    /// link, a reset token or a session identifier lives. Nothing here can tell one of those from
    /// <c>?lang=en</c>, so the whole class is refused rather than guessed at.
    /// </summary>
    public const string UrlCarriesQuery = "UrlCarriesQuery";

    /// <summary>A literal address the public internet cannot reach, or must not be asked to.</summary>
    public const string AddressNotPublic = "AddressNotPublic";

    /// <summary>
    /// A name only this network can resolve — <c>localhost</c>, a single label, or an explicitly
    /// internal suffix. Sending one to Google discloses an internal hostname for nothing.
    /// </summary>
    public const string HostNotPublic = "HostNotPublic";

    public static bool IsSupported(string value) =>
        value is UrlNotAbsolute or SchemeNotSupported or UrlCarriesCredentials
            or UrlCarriesQuery or AddressNotPublic or HostNotPublic;
}

/// <summary>
/// Whether one URL may be handed to a third-party auditor, and why not when it may not.
/// </summary>
/// <remarks>
/// The reason is carried rather than reduced to a boolean because it is shown to whoever tried to
/// enable the feature. "This endpoint cannot be audited" with no cause is the dead end this
/// project avoids elsewhere.
/// </remarks>
public sealed record PageAuditEligibilityResult(bool IsEligible, string? Reason)
{
    public static PageAuditEligibilityResult Eligible { get; } = new(true, null);

    public static PageAuditEligibilityResult Rejected(string reason) => new(false, reason);
}

/// <summary>
/// The public-only rule, as a pure function of the URL.
/// </summary>
/// <remarks>
/// <para>
/// This decides only what can be known from the URL itself. It deliberately does not resolve DNS
/// or contact the target: proving that Google can reach a page is Google's job, and doing it here
/// would add an outbound request to the scheduling transaction for an answer the provider gives
/// authoritatively a moment later.
/// </para>
/// <para>
/// The consequence of a wrong answer is not a failed check but a disclosure — an internal URL
/// handed to a third party and loaded by their infrastructure. So the rule refuses anything it
/// cannot positively establish is public, rather than allowing anything it cannot prove is private.
/// </para>
/// </remarks>
public static class PageAuditEligibility
{
    /// <summary>
    /// Suffixes reserved for names that never resolve on the public internet (RFC 6762, RFC 8375,
    /// RFC 6761). A page under one of these cannot be audited by anyone outside its own network.
    /// </summary>
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

        // Endpoint normalization preserves query strings, and this URL is handed to Google
        // verbatim. A query is exactly where a signed link, a password-reset token or a session
        // identifier lives, and no rule here can tell one of those from a locale switch. The
        // conservative reading is the only safe one: the cost of refusing "?lang=en" is an
        // endpoint that cannot be audited, and the cost of allowing a token is disclosing it to a
        // third party who then loads it.
        if (parsed.Query.Length > 0)
        {
            return PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.UrlCarriesQuery);
        }

        return EvaluateHost(parsed);
    }

    private static PageAuditEligibilityResult EvaluateHost(Uri parsed)
    {
        // IdnHost rather than Host: a unicode host and its punycode form are the same host, and
        // only one of them would match the suffix list.
        var host = parsed.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0)
        {
            return PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.HostNotPublic);
        }

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

        // A single label has no public registry behind it, so it can only be resolved by whoever
        // configured it locally. Google would resolve it to something else, or to nothing.
        if (!host.Contains('.'))
        {
            return PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.HostNotPublic);
        }

        return InternalSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal))
            ? PageAuditEligibilityResult.Rejected(PageAuditIneligibilityReasons.HostNotPublic)
            : PageAuditEligibilityResult.Eligible;
    }
}
