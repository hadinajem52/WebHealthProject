using WebHealth.Domain.Seo;

namespace WebHealth.Application.Seo;

/// <summary>
/// The bytes are the ones the transport already read; nothing here fetches anything.
/// </summary>
public sealed record SeoExtractionInput(
    bool TransportSucceeded,
    int? StatusCode,
    string? ContentType,
    string? FinalUrl,
    bool BodyTruncated,
    ReadOnlyMemory<byte> Body);

/// <summary>
/// BR-E10: extracted values and their observed lengths only. There is deliberately no member that
/// can carry the document, so no caller can persist or log one through this contract.
/// </summary>
public sealed record SeoExtraction(
    string Applicability,
    string? NotApplicableReason,
    bool DocumentTruncated,
    SeoValue Title,
    int TitleCount,
    SeoValue MetaDescription,
    int MetaDescriptionCount,
    SeoValue CanonicalHref,
    int CanonicalCount,
    string? CanonicalAbsoluteUrl,
    SeoValue RobotsMeta,
    int RobotsMetaCount)
{
    public bool IsApplicable => Applicability == SeoApplicabilities.Applicable;

    public static SeoExtraction NotApplicable(string reason, bool documentTruncated = false) => new(
        SeoApplicabilities.NotApplicable, reason, documentTruncated,
        SeoValue.None, 0, SeoValue.None, 0, SeoValue.None, 0, null, SeoValue.None, 0);
}

public interface ISeoValueExtractor
{
    SeoExtraction Extract(SeoExtractionInput input);
}
