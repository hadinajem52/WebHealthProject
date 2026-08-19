namespace WebHealth.Domain.Seo;

public static class SeoApplicabilities
{
    public const string Applicable = "Applicable";
    public const string NotApplicable = "NotApplicable";
}

/// <summary>
/// BR-E01. Why a check produced no SEO values, recorded rather than left as a missing row: an
/// operator has to be able to tell "this endpoint serves a PDF" from "this check never ran".
/// </summary>
public static class SeoNotApplicableReasons
{
    public const string TransportFailed = "TransportFailed";
    public const string NonSuccessStatus = "NonSuccessStatus";
    public const string NonHtml = "NonHtml";
    public const string EmptyBody = "EmptyBody";

    /// <summary>
    /// Parsing an untrusted document must never be able to lose a check. A parse that throws is
    /// recorded as a decision like any other, not propagated into the finalization transaction.
    /// </summary>
    public const string ExtractionFailed = "ExtractionFailed";

    public static bool IsSupported(string value) =>
        value is TransportFailed or NonSuccessStatus or NonHtml or EmptyBody or ExtractionFailed;
}

/// <summary>
/// Bounds on the values that are stored. The observed length of each value is recorded separately
/// and is never bounded, so a stored value that was cut short cannot misreport how long the real
/// one was (BR-E10).
/// </summary>
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

/// <summary>
/// One bounded value together with the length it actually had in the document.
/// </summary>
public readonly record struct SeoValue(string? Value, int Length)
{
    public static SeoValue None => new(null, 0);
}

public static class SeoExtractionRules
{
    private static readonly string[] HtmlMediaTypes = ["text/html", "application/xhtml+xml"];

    /// <summary>
    /// BR-E01: SEO checks run only on successful HTML responses and never parse binary content.
    /// Returns null when extraction should proceed, otherwise the reason it must not.
    /// </summary>
    public static string? NotApplicableReason(SeoApplicabilityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.TransportSucceeded) return SeoNotApplicableReasons.TransportFailed;
        if (input.StatusCode is not (>= 200 and <= 299)) return SeoNotApplicableReasons.NonSuccessStatus;
        if (!IsHtml(input.ContentType)) return SeoNotApplicableReasons.NonHtml;
        return input.BodyLength <= 0 ? SeoNotApplicableReasons.EmptyBody : null;
    }

    /// <summary>
    /// An absent Content-Type is not HTML. Guessing that unlabelled bytes are markup is the
    /// "parse binary content" BR-E01 forbids.
    /// </summary>
    public static bool IsHtml(string? contentType) =>
        MediaType(contentType) is { } mediaType
        && HtmlMediaTypes.Contains(mediaType, StringComparer.Ordinal);

    /// <summary>The media type with parameters stripped, lowercased; null when absent or malformed.</summary>
    public static string? MediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        var separator = contentType.IndexOf(';', StringComparison.Ordinal);
        var mediaType = (separator < 0 ? contentType : contentType[..separator]).Trim().ToLowerInvariant();
        return mediaType.Length == 0 ? null : mediaType;
    }

    /// <summary>The charset parameter of a Content-Type header, unquoted; null when absent.</summary>
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

    /// <summary>
    /// For human-readable values: collapses runs of whitespace and trims, then bounds the stored
    /// value while reporting the length the collapsed value actually had. An empty or
    /// whitespace-only value is no value.
    /// </summary>
    public static SeoValue BoundedText(string? raw, int maxLength) =>
        Bound(string.IsNullOrWhiteSpace(raw) ? null : Collapse(raw), maxLength);

    /// <summary>
    /// For URLs: trims only. A canonical href is kept as authored because it is diagnostic
    /// evidence — collapsing whitespace inside it would show the operator a value the page does
    /// not contain, and internal whitespace is exactly the authoring mistake worth seeing.
    /// </summary>
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

    /// <summary>
    /// BR-E04 needs the authored canonical value for diagnosis and its absolute form for the host
    /// comparison. Resolution against the URL the response was served from is a fact about the
    /// document; whether the resolved host is acceptable is policy, and is decided elsewhere.
    /// </summary>
    public static string? ResolveAbsolute(string? href, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        var candidate = href.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute) && IsHttpScheme(absolute))
        {
            return absolute.AbsoluteUri;
        }

        // Falling through rather than failing here is what makes a protocol-relative href work:
        // "//cdn.test/x" parses as an absolute file URI on its own, and only resolution against
        // the served URL gives it the right scheme. A genuinely non-HTTP scheme still resolves to
        // itself and is rejected below.
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin)
            && Uri.TryCreate(origin, candidate, out var resolved)
            && IsHttpScheme(resolved)
            ? resolved.AbsoluteUri
            : null;
    }

    private static bool IsHttpScheme(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
}
