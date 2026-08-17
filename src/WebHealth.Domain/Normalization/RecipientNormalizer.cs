using System.Globalization;

namespace WebHealth.Domain.Normalization;

/// <summary>
/// Trims and parses the address, normalizes the domain to IDNA ASCII, and preserves local-part
/// case. A configured case-insensitive local-part policy for known company/demo mailboxes is
/// deferred until a real deployment needs it; the version is stored regardless so that addition
/// does not silently rewrite delivery identity.
/// </summary>
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
