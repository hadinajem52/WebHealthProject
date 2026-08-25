namespace WebHealth.Domain.Seo;

public static class SeoApplicabilities
{
    public const string Applicable = "Applicable";
    public const string NotApplicable = "NotApplicable";
}

public static class SeoNotApplicableReasons
{
    public const string TransportFailed = "TransportFailed";
    public const string NonSuccessStatus = "NonSuccessStatus";
    public const string NonHtml = "NonHtml";
    public const string EmptyBody = "EmptyBody";

    public const string ExtractionFailed = "ExtractionFailed";

    public static bool IsSupported(string value) =>
        value is TransportFailed or NonSuccessStatus or NonHtml or EmptyBody or ExtractionFailed;
}

public static class SeoValueLimits
{
    public const int Title = 512;
    public const int MetaDescription = 1024;
    public const int CanonicalHref = 2048;
    public const int RobotsMeta = 256;
}

public sealed record SeoApplicabilityInput(
    bool TransportSucceeded,
    int? StatusCode,
    string? ContentType,
    long BodyLength);

public readonly record struct SeoValue(string? Value, int Length)
{
    public static SeoValue None => new(null, 0);
}

public static class SeoExtractionRules
{
    private static readonly string[] HtmlMediaTypes = ["text/html", "application/xhtml+xml"];

    public static string? NotApplicableReason(SeoApplicabilityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.TransportSucceeded) return SeoNotApplicableReasons.TransportFailed;
        if (input.StatusCode is not (>= 200 and <= 299)) return SeoNotApplicableReasons.NonSuccessStatus;
        if (!IsHtml(input.ContentType)) return SeoNotApplicableReasons.NonHtml;
        return input.BodyLength <= 0 ? SeoNotApplicableReasons.EmptyBody : null;
    }

    public static bool IsHtml(string? contentType) =>
        MediaType(contentType) is { } mediaType
        && HtmlMediaTypes.Contains(mediaType, StringComparer.Ordinal);

    public static string? MediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        var separator = contentType.IndexOf(';', StringComparison.Ordinal);
        var mediaType = (separator < 0 ? contentType : contentType[..separator]).Trim().ToLowerInvariant();
        return mediaType.Length == 0 ? null : mediaType;
    }

    public static string? CharSet(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        foreach (var parameter in contentType.Split(';', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var pair = parameter.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && pair[0].Equals("charset", StringComparison.OrdinalIgnoreCase))
            {
                var value = pair[1].Trim('"').Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    public static SeoValue BoundedText(string? raw, int maxLength) =>
        Bound(string.IsNullOrWhiteSpace(raw) ? null : Collapse(raw), maxLength);

    public static SeoValue BoundedUrl(string? raw, int maxLength) =>
        Bound(string.IsNullOrWhiteSpace(raw) ? null : raw.Trim(), maxLength);

    private static SeoValue Bound(string? value, int maxLength) =>
        string.IsNullOrEmpty(value)
            ? SeoValue.None
            : new(value.Length <= maxLength ? value : value[..maxLength], value.Length);

    private static string Collapse(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    public static string? ResolveAbsolute(string? href, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        var candidate = href.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute) && IsHttpScheme(absolute))
        {
            return absolute.AbsoluteUri;
        }

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin)
            && Uri.TryCreate(origin, candidate, out var resolved)
            && IsHttpScheme(resolved)
            ? resolved.AbsoluteUri
            : null;
    }

    private static bool IsHttpScheme(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
}
