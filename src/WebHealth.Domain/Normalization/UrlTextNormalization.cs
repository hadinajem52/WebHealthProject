using System.Globalization;
using System.Text;

namespace WebHealth.Domain.Normalization;

/// <summary>
/// The host and escape transformations shared by endpoint identity and crawl identity. They live
/// in one place because a URL that means one thing to a monitor and another to a crawl of the same
/// site is a defect neither side can see: the two would disagree about which page they are looking
/// at while both looked correct on their own.
/// </summary>
public static class UrlTextNormalization
{
    /// <summary>IDNA-ASCII, lowercased, trailing dot removed, IPv6 literals re-bracketed.</summary>
    public static string Host(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var host = BareHost(uri);
        return uri.HostNameType == UriHostNameType.IPv6 ? $"[{host}]" : host;
    }

    /// <summary>The host without IPv6 brackets, for comparison against a stored host value.</summary>
    public static string BareHost(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IdnHost.TrimEnd('.').ToLowerInvariant();
    }

    public static string Port(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsDefaultPort ? string.Empty : $":{uri.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Decodes percent-encoded unreserved characters and uppercases the escapes that remain, so
    /// <c>%7Ea</c>, <c>%7ea</c> and <c>~a</c> are one string. An escape that is not a valid pair is
    /// left exactly as authored rather than repaired: a malformed URL is evidence, not a mistake to
    /// guess at.
    /// </summary>
    public static string Escapes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%' || index + 2 >= value.Length
                || !byte.TryParse(value.AsSpan(index + 1, 2), NumberStyles.HexNumber, null, out var decoded))
            {
                result.Append(value[index]);
                continue;
            }

            var character = (char)decoded;
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~')
            {
                result.Append(character);
            }
            else
            {
                result.Append('%').Append(value[index + 1].ToString().ToUpperInvariant())
                    .Append(value[index + 2].ToString().ToUpperInvariant());
            }

            index += 2;
        }

        return result.ToString();
    }

    /// <summary>The escaped path with dot segments already resolved by <see cref="Uri" />.</summary>
    public static string Path(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var path = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        return path.Length == 0 ? "/" : $"/{Escapes(path)}";
    }

    /// <summary>The query without its leading <c>?</c>; empty when the URL carries none.</summary>
    public static string Query(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.GetComponents(UriComponents.Query, UriFormat.UriEscaped);
    }

    public static bool IsHttpScheme(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }
}
