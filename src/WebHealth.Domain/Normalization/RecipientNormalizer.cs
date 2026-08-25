using System.Globalization;

namespace WebHealth.Domain.Normalization;

public static class RecipientNormalizer
{
    public const short Version = 1;

    public static string? Normalize(string? rawAddress)
    {
        var trimmed = rawAddress?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var atIndex = trimmed.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == trimmed.Length - 1)
        {
            return null;
        }

        var localPart = trimmed[..atIndex];
        var domainPart = trimmed[(atIndex + 1)..];
        string asciiDomain;
        try
        {
            asciiDomain = new IdnMapping().GetAscii(domainPart).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }

        return $"{localPart}@{asciiDomain}";
    }
}
