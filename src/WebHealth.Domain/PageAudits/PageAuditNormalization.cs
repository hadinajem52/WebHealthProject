using System.Globalization;

namespace WebHealth.Domain.PageAudits;

/// <summary>
/// Turns one provider response into values this application is willing to store. Pure, so every
/// mapping below is decided by a unit test rather than by whatever a live audit happened to return.
/// </summary>
public static class PageAuditNormalization
{
    /// <summary>
    /// A score outside 0-1 is not a low score, it is a response we do not understand. Returning
    /// null rather than clamping keeps the run from recording a number the provider never sent.
    /// </summary>
    public static decimal? NormalizeCategoryScore(decimal? rawScore) =>
        rawScore is { } score && score >= 0m && score <= 1m ? score : null;

    /// <summary>
    /// The 0-100 number a reader sees. Away-from-zero so 0.995 reads as 100 and 0.005 as 1, and
    /// so the rule is one documented choice rather than banker's rounding arriving by default.
    /// </summary>
    public static int ToDisplayScore(decimal rawScore) =>
        (int)Math.Round(rawScore * 100m, MidpointRounding.AwayFromZero);

    public static int? ToDisplayScore(decimal? rawScore) =>
        rawScore is { } score ? ToDisplayScore(score) : null;

    /// <summary>
    /// One Lighthouse audit's meaning, from its display mode and score.
    /// <para>
    /// An audit carrying an error message is an <c>Error</c> whatever its mode claims: the audit
    /// did not run, and reporting that as a failing page would blame the site for our own or the
    /// provider's problem. A <c>binary</c> audit with no usable score is the same case — the mode
    /// promises a pass or a fail and delivered neither.
    /// </para>
    /// <para>
    /// An unrecognised mode becomes <c>Error</c>, never <c>Failed</c>. Lighthouse can add a mode in
    /// any release, and the safe default for something we cannot interpret is to say so rather
    /// than to invent a failing audit the page does not have.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Only a failed automated audit counts against the page. Manual, informative, not-applicable
    /// and errored audits are excluded, which is why the counts on the page and the score can
    /// disagree without either being wrong.
    /// </summary>
    public static bool CountsAsFailure(string status) => status == PageAuditItemStatuses.Failed;

    /// <summary>
    /// Cuts provider text to what the column can hold. Truncation is marked, because a description
    /// silently cut mid-sentence reads as the provider's own wording.
    /// </summary>
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

    /// <summary>
    /// Run warnings as one bounded line. The provider sends a list and the run stores a summary:
    /// the count is what a reader acts on, and the raw list is unbounded provider text.
    /// </summary>
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

    /// <summary>
    /// The major version, for comparability. Lighthouse versions are dotted; anything that does not
    /// start with a number has no major version we can reason about, so comparison treats it as
    /// changed rather than assuming it matches.
    /// </summary>
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
