namespace WebHealth.Infrastructure.PngAudits;

internal static class PngSourceMappingIdentity
{
    public static string Create(
        string imageIdentityHash,
        string sourcePageIdentityHash,
        string attributeKind,
        string? descriptor) =>
        string.Join(
            '|',
            imageIdentityHash.ToUpperInvariant(),
            sourcePageIdentityHash.ToUpperInvariant(),
            Bound(attributeKind, PngAuditTextBounds.AttributeKind),
            Bound(descriptor, PngAuditTextBounds.Descriptor) ?? string.Empty);

    private static string? Bound(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength ? value : value[..maximumLength];
}
