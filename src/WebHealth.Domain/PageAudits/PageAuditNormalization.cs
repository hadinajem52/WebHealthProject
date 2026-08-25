using System.Globalization;

namespace WebHealth.Domain.PageAudits;

public static class PageAuditNormalization
{
    public static decimal? NormalizeCategoryScore(decimal? rawScore) =>
        rawScore is { } score && score >= 0m && score <= 1m ? score : null;

    public static int ToDisplayScore(decimal rawScore) =>
        (int)Math.Round(rawScore * 100m, MidpointRounding.AwayFromZero);

    public static int? ToDisplayScore(decimal? rawScore) =>
        rawScore is { } score ? ToDisplayScore(score) : null;

    public static string ClassifyAuditStatus(string? scoreDisplayMode, decimal? score, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            return PageAuditItemStatuses.Error;
        }

        return scoreDisplayMode switch
        {
            PageAuditScoreDisplayModes.Binary => ClassifyBinary(score),
            PageAuditScoreDisplayModes.Numeric => score is >= 0m and <= 1m
                ? PageAuditItemStatuses.Scored
                : PageAuditItemStatuses.Error,
            PageAuditScoreDisplayModes.Manual => PageAuditItemStatuses.Manual,
            PageAuditScoreDisplayModes.NotApplicable => PageAuditItemStatuses.NotApplicable,
            PageAuditScoreDisplayModes.Informative => PageAuditItemStatuses.Informative,
            PageAuditScoreDisplayModes.Error => PageAuditItemStatuses.Error,
            _ => PageAuditItemStatuses.Error
        };
    }

    public static bool CountsAsFailure(string status) => status == PageAuditItemStatuses.Failed;

    public static string? BoundText(string? value, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 4);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var collapsed = value.Trim();
        return collapsed.Length <= maxLength
            ? collapsed
            : string.Concat(collapsed.AsSpan(0, maxLength - 1), "…");
    }

    public static string? SummarizeWarnings(IReadOnlyList<string>? warnings, int maxLength)
    {
        if (warnings is null || warnings.Count == 0)
        {
            return null;
        }

        var joined = string.Join(" | ", warnings.Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim()));
        return BoundText(joined, maxLength);
    }

    public static int? MajorVersionOf(string? lighthouseVersion)
    {
        if (string.IsNullOrWhiteSpace(lighthouseVersion))
        {
            return null;
        }

        var firstSegment = lighthouseVersion.Split('.', 2)[0].Trim();
        return int.TryParse(firstSegment, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            ? major
            : null;
    }

    private static string ClassifyBinary(decimal? score) => score switch
    {
        null => PageAuditItemStatuses.Error,
        1m => PageAuditItemStatuses.Passed,
        >= 0m and < 1m => PageAuditItemStatuses.Failed,
        _ => PageAuditItemStatuses.Error
    };
}
