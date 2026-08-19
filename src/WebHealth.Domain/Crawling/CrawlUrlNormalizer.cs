using WebHealth.Domain.Normalization;

namespace WebHealth.Domain.Crawling;

/// <summary>
/// How a run treats query strings (BR-L04). Documented in
/// docs/phase-6/Crawl_Scope_And_URL_Identity.md; the default is deliberate, not incidental.
/// </summary>
public enum CrawlQueryPolicy
{
    /// <summary>Drop tracking parameters, then sort the survivors. The default.</summary>
    Canonicalize = 0,

    /// <summary>Drop tracking parameters, keep the authored order.</summary>
    PreserveOrder = 1,

    /// <summary>Drop the query entirely; the path alone is the key.</summary>
    Ignore = 2
}

/// <summary>Why a URL is not a crawl target. Recorded, never inferred from a null.</summary>
public static class CrawlUrlRejections
{
    public const string Malformed = "Malformed";
    public const string UnsupportedScheme = "UnsupportedScheme";
    public const string CredentialsPresent = "CredentialsPresent";
    public const string TooLong = "TooLong";

    /// <summary>The parameter cap of section 2.2: a generated permutation, not an authored page.</summary>
    public const string TooManyQueryParameters = "TooManyQueryParameters";
}

/// <summary>
/// The run-level inputs to URL identity. They are inputs rather than constants because a site that
/// genuinely routes on <c>ref</c> has to be able to say so without a code change.
/// </summary>
public sealed record CrawlUrlOptions
{
    public const int DefaultMaxQueryParameters = 12;
    public const int MaxUrlLength = 2048;

    /// <summary>
    /// Set by the referrer, never by the resource, so two URLs differing only in these are one
    /// page. <c>utm_</c> is matched as a prefix so a new member of that family needs no change.
    /// </summary>
    public static IReadOnlySet<string> DefaultTrackingParameters { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gclid", "fbclid", "msclkid", "dclid", "yclid", "igshid",
            "mc_eid", "mc_cid", "_ga", "_gl", "ref", "referrer"
        };

    public const string TrackingParameterPrefix = "utm_";

    public static CrawlUrlOptions Default { get; } = new();

    public CrawlQueryPolicy QueryPolicy { get; init; } = CrawlQueryPolicy.Canonicalize;

    public IReadOnlySet<string> TrackingParameters { get; init; } = DefaultTrackingParameters;

    public int MaxQueryParameters { get; init; } = DefaultMaxQueryParameters;
}

/// <summary>
/// One canonical crawl URL, decomposed so the frontier can apply its per-path caps without
/// re-parsing the string it was just handed.
/// </summary>
public sealed record CrawlUrl(string Value, string Host, int Port, string Path, bool HasQuery)
{
    /// <summary>Scheme, host and effective port — the same key <c>robots_snapshot</c> is stored by.</summary>
    public string Origin => Value[..PathStart];

    private int PathStart =>
        (HasQuery ? Value.IndexOf('?', StringComparison.Ordinal) : Value.Length) - Path.Length;

    /// <summary>The path with its last segment removed, used to derive a seed's default prefix.</summary>
    public string Directory
    {
        get
        {
            var lastSlash = Path.LastIndexOf('/');
            return lastSlash <= 0 ? "/" : Path[..(lastSlash + 1)];
        }
    }
}

public readonly record struct CrawlUrlResult(CrawlUrl? Url, string? Rejection)
{
    public bool Succeeded => Url is not null;

    public static CrawlUrlResult Rejected(string rejection) => new(null, rejection);
}

/// <summary>
/// BR-L03 and BR-L04. The canonical crawl URL is the revisit key: normalise too little and the
/// crawl does not terminate against someone else's site; normalise too much and it silently misses
/// real pages. Every rule here is a pure function of a string and the run's options.
/// </summary>
public static class CrawlUrlNormalizer
{
    /// <summary>
    /// Resolves an authored <c>href</c> against the page it was found on, then canonicalises it.
    /// A relative href is meaningless without its base, so resolution and canonicalisation are one
    /// operation rather than two a caller could forget to compose.
    /// </summary>
    public static CrawlUrlResult Resolve(string? href, CrawlUrl baseUrl, CrawlUrlOptions options)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        if (string.IsNullOrWhiteSpace(href)) return CrawlUrlResult.Rejected(CrawlUrlRejections.Malformed);

        // Authored hrefs routinely carry newlines and tabs from wrapped markup. They are not part
        // of the URL, and leaving them in turns one target into several.
        var candidate = new string([.. href.Where(character => !char.IsControl(character))]).Trim();
        if (candidate.Length == 0) return CrawlUrlResult.Rejected(CrawlUrlRejections.Malformed);

        if (!Uri.TryCreate(baseUrl.Value, UriKind.Absolute, out var origin)
            || !Uri.TryCreate(origin, candidate, out var resolved))
        {
            return CrawlUrlResult.Rejected(CrawlUrlRejections.Malformed);
        }

        return Canonicalize(resolved, options);
    }

    /// <summary>Canonicalises an absolute URL, for seeds and for redirect targets.</summary>
    public static CrawlUrlResult Normalize(string? url, CrawlUrlOptions options)
    {
        if (string.IsNullOrWhiteSpace(url)) return CrawlUrlResult.Rejected(CrawlUrlRejections.Malformed);
        var candidate = url.Trim();
        return candidate.Length > CrawlUrlOptions.MaxUrlLength
            ? CrawlUrlResult.Rejected(CrawlUrlRejections.TooLong)
            : Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                ? Canonicalize(uri, options)
                : CrawlUrlResult.Rejected(CrawlUrlRejections.Malformed);
    }

    private static CrawlUrlResult Canonicalize(Uri uri, CrawlUrlOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // A page may link to mailto:, tel:, javascript: or data:. Dereferencing one is not a crawl
        // and, for javascript:, is not even a request — they are rejected before anything else.
        if (!UrlTextNormalization.IsHttpScheme(uri))
        {
            return CrawlUrlResult.Rejected(CrawlUrlRejections.UnsupportedScheme);
        }

        if (uri.Host.Length == 0 || uri.Host.Contains('%', StringComparison.Ordinal))
        {
            return CrawlUrlResult.Rejected(CrawlUrlRejections.Malformed);
        }

        // Credentials in a link are not identity, and following one would transmit them.
        if (uri.UserInfo.Length > 0)
        {
            return CrawlUrlResult.Rejected(CrawlUrlRejections.CredentialsPresent);
        }

        var query = ApplyQueryPolicy(UrlTextNormalization.Query(uri), options);
        if (query.Rejection is not null) return CrawlUrlResult.Rejected(query.Rejection);

        // The fragment is dropped by construction: it is never read back from the Uri. #top is a
        // position inside a document, not a document (BR-L03).
        var path = UrlTextNormalization.Path(uri);
        var authority = $"{UrlTextNormalization.Host(uri)}{UrlTextNormalization.Port(uri)}";
        var value = $"{uri.Scheme.ToLowerInvariant()}://{authority}{path}"
            + (query.Value.Length == 0 ? string.Empty : $"?{query.Value}");

        return value.Length > CrawlUrlOptions.MaxUrlLength
            ? CrawlUrlResult.Rejected(CrawlUrlRejections.TooLong)
            : new(new(value, UrlTextNormalization.BareHost(uri), uri.Port, path, query.Value.Length > 0), null);
    }

    private static (string Value, string? Rejection) ApplyQueryPolicy(string query, CrawlUrlOptions options)
    {
        if (options.QueryPolicy == CrawlQueryPolicy.Ignore || query.Length == 0)
        {
            return (string.Empty, null);
        }

        var kept = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(UrlTextNormalization.Escapes)
            .Select(parameter => new QueryParameter(NameOf(parameter), parameter))
            .Where(parameter => !IsTracking(parameter.Name, options))
            .ToArray();

        // The cap counts what survives: a URL carrying twenty utm parameters canonicalises to none
        // and is an ordinary page, not a permutation.
        if (kept.Length > options.MaxQueryParameters)
        {
            return (string.Empty, CrawlUrlRejections.TooManyQueryParameters);
        }

        if (options.QueryPolicy == CrawlQueryPolicy.Canonicalize)
        {
            kept = [.. kept
                .OrderBy(parameter => parameter.Name, StringComparer.Ordinal)
                .ThenBy(parameter => parameter.Text, StringComparer.Ordinal)];
        }

        return (string.Join('&', kept.Select(parameter => parameter.Text)), null);
    }

    private static bool IsTracking(string name, CrawlUrlOptions options) =>
        name.StartsWith(CrawlUrlOptions.TrackingParameterPrefix, StringComparison.OrdinalIgnoreCase)
        || options.TrackingParameters.Contains(name);

    private static string NameOf(string parameter)
    {
        var separator = parameter.IndexOf('=', StringComparison.Ordinal);
        return separator < 0 ? parameter : parameter[..separator];
    }

    private readonly record struct QueryParameter(string Name, string Text);
}
