using System.Globalization;

namespace WebHealth.Domain.Normalization;

public static class EmailAddressRules
{
    public const int MaxAddressLength = 320;
    private const int MaxLocalPartLength = 64;
    private const int MaxDomainLength = 255;
    private const int MaxLabelLength = 63;
    private const string AllowedLocalPartSymbols = "!#$%&'*+-/=?^_`{|}~";

    public static bool IsValid(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxAddressLength)
        {
            return false;
        }

        var atIndex = candidate.IndexOf('@');
        if (atIndex <= 0 || atIndex != candidate.LastIndexOf('@'))
        {
            return false;
        }

        return IsValidLocalPart(candidate[..atIndex])
            && IsValidDomain(candidate[(atIndex + 1)..]);
    }

    private static bool IsValidLocalPart(string localPart)
    {
        if (localPart.Length is 0 or > MaxLocalPartLength
            || localPart[0] == '.'
            || localPart[^1] == '.'
            || localPart.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in localPart)
        {
            if (!char.IsAsciiLetterOrDigit(character)
                && character != '.'
                && !AllowedLocalPartSymbols.Contains(character, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidDomain(string domainPart)
    {
        if (domainPart.Length is 0 or > MaxDomainLength)
        {
            return false;
        }

        string asciiDomain;
        try
        {
            asciiDomain = new IdnMapping().GetAscii(domainPart);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var labels = asciiDomain.Split('.');
        if (labels.Length < 2)
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (label.Length is 0 or > MaxLabelLength || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }

            foreach (var character in label)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character != '-')
                {
                    return false;
                }
            }
        }

        return true;
    }
}
