using System.Globalization;
using System.Text.Json;
using WebHealth.Application.PageAudits;
using WebHealth.Domain.PageAudits;

namespace WebHealth.Infrastructure.PageAudits;

internal sealed class PageAuditResponseReader(PageSpeedInsightsOptions options)
{
    public PageAuditProviderBatchResult Read(
        JsonDocument document,
        string requestedUrl,
        IReadOnlyList<string> requestedCategories)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderContractInvalid,
                "The provider response was not a JSON object.");
        }

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
            || categories.ValueKind != JsonValueKind.Object)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderContractInvalid,
                "The provider response carried no Lighthouse categories.");
        }

        var lighthouseVersion = TryGetString(lighthouse, "lighthouseVersion");
        if (string.IsNullOrWhiteSpace(lighthouseVersion))
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderContractInvalid,
                "The provider response carried no Lighthouse version. Without it a stored score "
                + "cannot be compared against a later one.");
        }

        var providerRequestedUrl = TryGetString(lighthouse, "requestedUrl") ?? requestedUrl;
        var finalUrl = TryGetString(lighthouse, "finalUrl") ?? providerRequestedUrl;
        var analysisAt = ReadAnalysisTimestamp(root, lighthouse);
        var warnings = ReadWarnings(lighthouse);
        var results = new Dictionary<string, PageAuditProviderResult>(StringComparer.Ordinal);
        foreach (var requestedCategory in requestedCategories)
        {
            var categoryParameter = PageAuditCategories.ToParameter(requestedCategory);
            if (!categories.TryGetProperty(categoryParameter, out var category)
                || category.ValueKind != JsonValueKind.Object)
            {
                throw new PageAuditProviderException(
                    PageAuditFailureCategories.ProviderContractInvalid,
                    $"The provider response carried no {requestedCategory} category. The request asks for one "
                    + "explicitly, so a response without it is not a result we can store.");
            }

            var rawScore = PageAuditNormalization.NormalizeCategoryScore(TryGetDecimal(category, "score"));
            if (rawScore is null)
            {
                throw new PageAuditProviderException(
                    PageAuditFailureCategories.ProviderContractInvalid,
                    $"The {requestedCategory} category carried no score inside the provider's own 0-1 range.");
            }

            results.Add(requestedCategory, new PageAuditProviderResult(
                PageAuditProviders.PageSpeedInsights,
                providerRequestedUrl,
                finalUrl,
                analysisAt,
                lighthouseVersion,
                rawScore,
                ReadItems(lighthouse, category, requestedCategory),
                warnings,
                null,
                null));
        }

        return new PageAuditProviderBatchResult(results);
    }

    private IReadOnlyList<PageAuditProviderItem> ReadItems(
        JsonElement lighthouse,
        JsonElement category,
        string requestedCategory)
    {
        if (!category.TryGetProperty("auditRefs", out var auditRefs)
            || auditRefs.ValueKind != JsonValueKind.Array)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderContractInvalid,
                $"The {requestedCategory} category listed no audits.");
        }

        if (auditRefs.GetArrayLength() > options.MaximumAuditCount)
        {
            throw new PageAuditProviderException(
                PageAuditFailureCategories.ProviderResponseTooLarge,
                $"The {requestedCategory} category declared {auditRefs.GetArrayLength()} audits, above the "
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
                    $"The {requestedCategory} category referenced an audit with no identifier.");
            }

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
                    $"The {requestedCategory} category referenced the audit {Sanitize(auditId, 60)}, which the "
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
                TryGetString(audit, "errorMessage"),
                TryGetDecimal(audit, "numericValue"),
                TryGetString(audit, "numericUnit")));
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

        return [.. warnings.EnumerateArray()
            .Where(warning => warning.ValueKind == JsonValueKind.String)
            .Select(warning => warning.GetString()!)
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Take(20)];
    }

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
