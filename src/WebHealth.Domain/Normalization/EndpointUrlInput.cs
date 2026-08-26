namespace WebHealth.Domain.Normalization;

public enum EndpointUrlInputKind
{
    Empty,
    SchemeProvided,
    SchemeMissing
}

public sealed record EndpointUrlInterpretation(
    EndpointUrlInputKind Kind,
    string RawValue,
    string? HttpsCandidate,
    string? HttpCandidate)
{
    public bool NeedsSchemeProbe =>
        Kind == EndpointUrlInputKind.SchemeMissing && HttpsCandidate is not null;
}

public static class EndpointUrlInput
{
    public static EndpointUrlInterpretation Interpret(string? value)
    {
        var raw = value?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            return new(EndpointUrlInputKind.Empty, raw, null, null);
        }

        var authority = ExtractAuthority(raw);
        return authority is null
            ? new(EndpointUrlInputKind.SchemeProvided, raw, null, null)
            : new(
                EndpointUrlInputKind.SchemeMissing,
                raw,
                Candidate(Uri.UriSchemeHttps, authority),
                Candidate(Uri.UriSchemeHttp, authority));
    }

    private static string? ExtractAuthority(string value)
    {
        if (HasSchemePrefix(value))
        {
            return null;
        }

        var authority = value.StartsWith("//", StringComparison.Ordinal) ? value[2..] : value;
        return authority.Length == 0 || authority[0] == '/' ? null : authority;
    }

    private static string? Candidate(string scheme, string authority)
    {
        var result = EndpointUrlNormalizer.Normalize($"{scheme}://{authority}");
        return result.Succeeded ? result.NormalizedUrl : null;
    }

    private static bool HasSchemePrefix(string value)
    {
        var delimiter = value.IndexOf("://", StringComparison.Ordinal);
        if (delimiter <= 0)
        {
            return false;
        }

        var scheme = value.AsSpan(0, delimiter);
        if (!char.IsAsciiLetter(scheme[0]))
        {
            return false;
        }

        foreach (var character in scheme)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
