using WebHealth.Domain.Normalization;

namespace WebHealth.Domain.Crawling;

public enum CrawlQueryPolicy
{
    Canonicalize = 0,

    PreserveOrder = 1,

    Ignore = 2
}

public static class CrawlUrlRejections
{
    public const string Malformed = "Malformed";
    public const string UnsupportedScheme = "UnsupportedScheme";
    public const string CredentialsPresent = "CredentialsPresent";
    public const string TooLong = "TooLong";

    public const string TooManyQueryParameters = "TooManyQueryParameters";
}

public sealed record CrawlUrlOptions
{
    public const int DefaultMaxQueryParameters = 12;
    public const int MaxUrlLength = 2048;

    public static IReadOnlySet<string> DefaultTrackingParameters { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gclid", "fbclid", "msclkid", "dclid", "yclid", "igshid",
            "mc_eid", "mc_cid", "_ga", "_gl", "ref", "referrer"
        };

    public const string TrackingParameterPrefix = "utm_";

    public static IReadOnlySet<string> DefaultSensitiveQueryParameters { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "access_token", "api_key", "apikey", "auth", "authorization", "client_secret", "code",
            "jwt", "password", "passwd", "refresh_token", "secret", "session", "sessionid", "sig",
            "signature", "token", "x-amz-signature", "x-goog-signature"
        };

    public static CrawlUrlOptions Default { get; } = new();

    public CrawlQueryPolicy QueryPolicy { get; init; } = CrawlQueryPolicy.Canonicalize;

    public IReadOnlySet<string> TrackingParameters { get; init; } = DefaultTrackingParameters;

    public IReadOnlySet<string> SensitiveQueryParameters { get; init; } =
        DefaultSensitiveQueryParameters;

    public int MaxQueryParameters { get; init; } = DefaultMaxQueryParameters;
}

public static class CrawlUrlRedactor
{
    public static string? Redact(string? url, CrawlUrlOptions options)
    {
        if (url is null) return null;
        var queryStart = url.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0 || queryStart == url.Length - 1) return url;

        var prefix = url[..(queryStart + 1)];
        var parameters = url[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries);
        var redacted = parameters.Select(parameter => RedactParameter(parameter, options));
        return prefix + string.Join('&', redacted);
    }

    private static string RedactParameter(string parameter, CrawlUrlOptions options)
    {
        var separator = parameter.IndexOf('=', StringComparison.Ordinal);
        var name = separator < 0 ? parameter : parameter[..separator];
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(name);
        }
        catch (UriFormatException)
        {
            decoded = name;
        }

        return options.SensitiveQueryParameters.Contains(decoded) ? $"{name}=REDACTED" : parameter;
    }
}

public sealed record CrawlUrl(string Value, string Host, int Port, string Path, bool HasQuery)
{
    public string Origin => Value[..PathStart];

    private int PathStart =>
        (HasQuery ? Value.IndexOf('?', StringComparison.Ordinal) : Value.Length) - Path.Length;

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

public static class CrawlUrlNormalizer
{
    public static CrawlUrlResult Resolve(string? href, CrawlUrl baseUrl, CrawlUrlOptions options)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        if (string.IsNullOrWhiteSpace(href)) return CrawlUrlResult.Rejected(CrawlUrlRejections.Malformed);

        var candidate = new string([.. href.Where(character => !char.IsControl(character))]).Trim();
        if (candidate.Length == 0) return CrawlUrlResult.Rejected(CrawlUrlRejections.Malformed);

        if (!Uri.TryCreate(baseUrl.Value, UriKind.Absolute, out var origin)
            || !Uri.TryCreate(origin, candidate, out var resolved))
        {
            return CrawlUrlResult.Rejected(CrawlUrlRejections.Malformed);
        }

        return Canonicalize(resolved, options);
    }

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

        if (!UrlTextNormalization.IsHttpScheme(uri))
        {
            return CrawlUrlResult.Rejected(CrawlUrlRejections.UnsupportedScheme);
        }

        if (uri.Host.Length == 0 || uri.Host.Contains('%', StringComparison.Ordinal)
            || !UrlTextNormalization.HasReadableHost(uri))
        {
            return CrawlUrlResult.Rejected(CrawlUrlRejections.Malformed);
        }

        if (uri.UserInfo.Length > 0)
        {
            return CrawlUrlResult.Rejected(CrawlUrlRejections.CredentialsPresent);
        }

        var query = ApplyQueryPolicy(UrlTextNormalization.Query(uri), options);
        if (query.Rejection is not null) return CrawlUrlResult.Rejected(query.Rejection);

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
