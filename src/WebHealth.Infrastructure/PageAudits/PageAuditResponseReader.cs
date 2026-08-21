using System.Globalization;
using System.Text.Json;
using WebHealth.Application.PageAudits;
using WebHealth.Domain.PageAudits;

namespace WebHealth.Infrastructure.PageAudits;

/// <summary>
/// Reads the handful of fields this feature needs out of a PageSpeed response and returns nothing
/// else.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a <see cref="JsonDocument" /> reader rather than a set of mirrored DTOs. The
/// response schema is large, mostly irrelevant here, and free to grow; mirroring it would mean
/// maintaining a model of Google's API instead of a model of what we store. Reading named paths
/// tolerates every addition Google makes without a change here.
/// </para>
/// <para>
/// Nothing in this file returns a <see cref="JsonElement" /> to a caller. That is what keeps the
/// "no provider type escapes Infrastructure" rule structural rather than a convention.
/// </para>
/// </remarks>
internal sealed class PageAuditResponseReader(PageSpeedInsightsOptions options)
{
    public PageAuditProviderResult Read(JsonDocument document, string requestedUrl)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.RootElement;

        // Guarded before anything reads a property off it. TryGetProperty throws rather than
        // returning false when the element is not an object, so a valid JSON array or scalar
        // would leave this reader by an exception nobody downstream is expecting.
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderContractInvalid,
                "The provider response was not a JSON object.");
        }

        // Checked before anything else. A blocked audit produced no result at all, and reading on
        // would report the absence of a score as a contract failure rather than as what it is.
        if (TryGetString(root, "captchaResult") is { } captcha
            && !captcha.Equals("CAPTCHA_NOT_NEEDED", StringComparison.Ordinal))
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.CaptchaBlocked,
                $"The provider reported a CAPTCHA result of {Sanitize(captcha, 60)}.");
        }

        if (!root.TryGetProperty("lighthouseResult", out var lighthouse)
            || lighthouse.ValueKind != JsonValueKind.Object)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderContractInvalid,
                "The provider response carried no lighthouseResult.");
        }

        // A runtime error is Lighthouse saying it could not audit the page. It outranks a missing
        // category, because the missing category is its consequence and the error is its cause.
        if (lighthouse.TryGetProperty("runtimeError", out var runtimeError)
            && runtimeError.ValueKind == JsonValueKind.Object
            && TryGetString(runtimeError, "code") is { } errorCode
            && !errorCode.Equals("NO_ERROR", StringComparison.Ordinal))
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.LighthouseRuntimeError,
                $"Lighthouse reported {Sanitize(errorCode, 60)}: "
                + $"{Sanitize(TryGetString(runtimeError, "message"), 400)}");
        }

        if (!lighthouse.TryGetProperty("categories", out var categories)
            || !categories.TryGetProperty(PageAuditCategories.SeoParameter, out var seoCategory)
            || seoCategory.ValueKind != JsonValueKind.Object)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderContractInvalid,
                "The provider response carried no SEO category. The request asks for one "
                + "explicitly, so a response without it is not a result we can store.");
        }

        var rawScore = PageAuditNormalization.NormalizeCategoryScore(TryGetDecimal(seoCategory, "score"));
        if (rawScore is null)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderContractInvalid,
                "The SEO category carried no score inside the provider's own 0-1 range.");
        }

        var lighthouseVersion = TryGetString(lighthouse, "lighthouseVersion");
        if (string.IsNullOrWhiteSpace(lighthouseVersion))
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderContractInvalid,
                "The provider response carried no Lighthouse version. Without it a stored score "
                + "cannot be compared against a later one.");
        }

        return new PageAuditProviderResult(
            PageAuditProviders.PageSpeedInsights,
            TryGetString(lighthouse, "requestedUrl") ?? requestedUrl,
            TryGetString(lighthouse, "finalUrl") ?? TryGetString(lighthouse, "requestedUrl") ?? requestedUrl,
            ReadAnalysisTimestamp(root, lighthouse),
            lighthouseVersion,
            rawScore,
            ReadItems(lighthouse, seoCategory),
            ReadWarnings(lighthouse),
            null,
            null);
    }

    /// <summary>
    /// The audits the SEO category actually references, in the order it references them.
    /// </summary>
    /// <remarks>
    /// Membership comes from <c>auditRefs</c>, never from iterating <c>audits</c>. The response
    /// carries audits belonging to other categories, and storing those would attribute them to a
    /// score they took no part in. A referenced audit that is missing is a broken contract rather
    /// than something to skip quietly: dropping it would leave a score with an unexplained gap.
    /// </remarks>
    private IReadOnlyList<PageAuditProviderItem> ReadItems(
        JsonElement lighthouse,
        JsonElement seoCategory)
    {
        if (!seoCategory.TryGetProperty("auditRefs", out var auditRefs)
            || auditRefs.ValueKind != JsonValueKind.Array)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderContractInvalid,
                "The SEO category listed no audits.");
        }

        if (auditRefs.GetArrayLength() > options.MaximumAuditCount)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderResponseTooLarge,
                $"The SEO category declared {auditRefs.GetArrayLength()} audits, above the "
                + $"configured ceiling of {options.MaximumAuditCount}.");
        }

        lighthouse.TryGetProperty("audits", out var audits);

        var items = new List<PageAuditProviderItem>(auditRefs.GetArrayLength());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in auditRefs.EnumerateArray())
        {
            var auditId = TryGetString(reference, "id");
            if (string.IsNullOrWhiteSpace(auditId))
            {
                throw new PageAuditProviderException(
                    PageAuditFailureCategories.ProviderContractInvalid,
                    "The SEO category referenced an audit with no identifier.");
            }

            // One row per audit per run is a database rule; a response that referenced the same
            // audit twice would fail at the insert with nothing explaining why.
            if (!seen.Add(auditId))
            {
                continue;
            }

            if (audits.ValueKind != JsonValueKind.Object
                || !audits.TryGetProperty(auditId, out var audit)
                || audit.ValueKind != JsonValueKind.Object)
            {
                throw new PageAuditProviderException(
                    PageAuditFailureCategories.ProviderContractInvalid,
                    $"The SEO category referenced the audit {Sanitize(auditId, 60)}, which the "
                    + "response does not contain.");
            }

            items.Add(new PageAuditProviderItem(
                auditId,
                TryGetString(audit, "title"),
                TryGetString(audit, "description"),
                TryGetDecimal(audit, "score"),
                TryGetString(audit, "scoreDisplayMode"),
                TryGetDouble(reference, "weight") ?? 0,
                TryGetString(reference, "group"),
                TryGetString(audit, "displayValue"),
                TryGetString(audit, "explanation"),
                TryGetString(audit, "errorMessage")));
        }

        return items;
    }

    private static IReadOnlyList<string> ReadWarnings(JsonElement lighthouse)
    {
        if (!lighthouse.TryGetProperty("runWarnings", out var warnings)
            || warnings.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        // Bounded here rather than at the summary: the array is provider-controlled, and reading
        // all of it into memory to join and then truncate is the allocation the cap exists to stop.
        return [.. warnings.EnumerateArray()
            .Where(warning => warning.ValueKind == JsonValueKind.String)
            .Select(warning => warning.GetString()!)
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Take(20)];
    }

    /// <summary>
    /// When the provider says it ran the audit. Falls back to the Lighthouse fetch time, and then
    /// to now: the run is real whether or not the provider dated it, and a null here would put a
    /// hole in a history the reader orders by.
    /// </summary>
    private static DateTimeOffset ReadAnalysisTimestamp(JsonElement root, JsonElement lighthouse) =>
        TryGetTimestamp(root, "analysisUTCTimestamp")
        ?? TryGetTimestamp(lighthouse, "fetchTime")
        ?? DateTimeOffset.UtcNow;

    private static DateTimeOffset? TryGetTimestamp(JsonElement element, string name) =>
        TryGetString(element, name) is { } text
        && DateTimeOffset.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static string? TryGetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static decimal? TryGetDecimal(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetDecimal(out var value)
            ? value
            : null;

    private static double? TryGetDouble(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetDouble(out var value)
        && double.IsFinite(value)
        && value >= 0
            ? value
            : null;

    /// <summary>
    /// Provider text on its way into a diagnostic. Bounded and stripped of control characters,
    /// because a diagnostic ends up in a log line and in a page.
    /// </summary>
    private static string Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(none)";
        }

        var cleaned = new string([.. value.Where(character =>
            !char.IsControl(character) || character == ' ')]).Trim();
        return PageAuditNormalization.BoundText(cleaned, maxLength) ?? "(none)";
    }
}
