using System.Globalization;
using System.Text;

namespace WebHealth.Domain.Normalization;

public static class UrlTextNormalization
{
    public static string Host(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var host = BareHost(uri);
        return uri.HostNameType == UriHostNameType.IPv6 ? $"[{host}]" : host;
    }

    public static string BareHost(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IdnHost.TrimEnd('.').ToLowerInvariant();
    }

    public static bool HasReadableHost(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        try
        {
            return uri.IdnHost.Length > 0;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    public static string Port(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsDefaultPort ? string.Empty : $":{uri.Port.ToString(CultureInfo.InvariantCulture)}";
    }

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

    public static string Path(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var path = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        return path.Length == 0 ? "/" : $"/{Escapes(path)}";
    }

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
