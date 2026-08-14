using System.Text;

namespace WebHealth.Domain.Normalization;

public static class NameNormalizer
{
    public const short Version = 1;

    public static string TrimDisplayName(string value) =>
        string.Join(' ', value.Normalize(NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static string Normalize(string value) => TrimDisplayName(value).ToUpperInvariant();
}
