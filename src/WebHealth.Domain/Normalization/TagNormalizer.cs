namespace WebHealth.Domain.Normalization;

public static class TagNormalizer
{
    public const int MaximumTagsPerWebsite = 20;
    public const int MaximumTagLength = 100;

    public static IReadOnlyList<NormalizedTag> Normalize(IEnumerable<string> values) =>
        values.Select(NameNormalizer.TrimDisplayName)
            .Where(value => value.Length > 0)
            .Select(value => new NormalizedTag(value, NameNormalizer.Normalize(value)))
            .DistinctBy(tag => tag.NormalizedName, StringComparer.Ordinal)
            .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public sealed record NormalizedTag(string Name, string NormalizedName);
