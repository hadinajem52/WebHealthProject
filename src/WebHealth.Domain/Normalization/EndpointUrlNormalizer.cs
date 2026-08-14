using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WebHealth.Domain.Normalization;

public static class EndpointUrlNormalizer
{
    public const short Version = 1;
    public const int MaximumLength = 2048;

    public static EndpointUrlNormalizationResult Normalize(string? value)
    {
        var displayUrl = value?.Trim() ?? string.Empty;
        var errors = ValidateInput(displayUrl);
        if (errors.Count > 0 || !Uri.TryCreate(displayUrl, UriKind.Absolute, out var uri))
        {
            return EndpointUrlNormalizationResult.Failure(
                errors.Count > 0 ? errors : ["Enter an absolute HTTP or HTTPS URL."]);
        }

        errors = ValidateUri(uri);
        if (errors.Count > 0)
        {
            return EndpointUrlNormalizationResult.Failure(errors);
        }

        var normalizedUrl = BuildNormalizedUrl(uri);
        return EndpointUrlNormalizationResult.Success(
            displayUrl,
            normalizedUrl,
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUrl)),
            uri.IdnHost.TrimEnd('.').ToLowerInvariant(),
            uri.Port);
    }

    private static List<string> ValidateInput(string value)
    {
        if (value.Length == 0)
        {
            return ["Enter an endpoint URL."];
        }

        return value.Length > MaximumLength
            ? [$"The endpoint URL cannot exceed {MaximumLength} characters."]
            : [];
    }

    private static List<string> ValidateUri(Uri uri)
    {
        var errors = new List<string>();
        if (uri.Scheme is not ("http" or "https"))
        {
            errors.Add("Only HTTP and HTTPS endpoint URLs are supported.");
        }

        if (uri.Host.Length == 0)
        {
            errors.Add("Enter an absolute URL with a host.");
        }

        if (uri.UserInfo.Length > 0)
        {
            errors.Add("Endpoint URLs cannot contain credentials.");
        }

        if (uri.Fragment.Length > 0)
        {
            errors.Add("Endpoint URLs cannot contain fragments.");
        }

        if (uri.Host.Contains('%', StringComparison.Ordinal))
        {
            errors.Add("Endpoint hosts cannot contain an IPv6 zone identifier.");
        }

        return errors;
    }

    private static string BuildNormalizedUrl(Uri uri)
    {
        var scheme = uri.Scheme.ToLowerInvariant();
        var host = NormalizeHost(uri);
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port.ToString(CultureInfo.InvariantCulture)}";
        var path = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        path = path.Length == 0 ? "/" : $"/{NormalizeEscapes(path)}";
        var query = uri.GetComponents(UriComponents.Query, UriFormat.UriEscaped);
        return query.Length == 0
            ? $"{scheme}://{host}{port}{path}"
            : $"{scheme}://{host}{port}{path}?{NormalizeEscapes(query)}";
    }

    private static string NormalizeHost(Uri uri)
    {
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return uri.HostNameType == UriHostNameType.IPv6 ? $"[{host}]" : host;
    }

    private static string NormalizeEscapes(string value)
    {
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
}

public sealed record EndpointUrlNormalizationResult(
    bool Succeeded,
    string? DisplayUrl,
    string? NormalizedUrl,
    byte[]? NormalizedUrlHash,
    IReadOnlyList<string> Errors,
    string? NormalizedHost,
    int? EffectivePort)
{
    internal static EndpointUrlNormalizationResult Success(
        string displayUrl,
        string normalizedUrl,
        byte[] hash,
        string normalizedHost,
        int effectivePort) => new(true, displayUrl, normalizedUrl, hash, [], normalizedHost, effectivePort);

    internal static EndpointUrlNormalizationResult Failure(
        IReadOnlyList<string> errors) => new(false, null, null, null, errors, null, null);
}
