using System.Security.Cryptography;
using System.Text;
using WebHealth.Application.PngAudits;
using WebHealth.Application.SiteAnalysis;
using WebHealth.Domain.Crawling;
using WebHealth.Domain.Normalization;

namespace WebHealth.Infrastructure.PngAudits;

internal sealed class PngAssetScope(IReadOnlyList<CrawlHostRule> allowedHosts)
{
    private readonly IReadOnlyList<CrawlHostRule> _allowedHosts =
        allowedHosts.Count > 0 ? [.. allowedHosts] : throw new ArgumentException(
            "An asset scope needs at least one allowed host.", nameof(allowedHosts));

    public bool Allows(string host) => _allowedHosts.Any(rule => rule.Matches(host));
}

internal sealed record PngResolvedAsset(
    string FetchUrl,
    string DisplayUrl,
    string IdentityHash,
    string Host);

internal readonly record struct PngAssetResolution(
    PngResolvedAsset? Asset,
    PngDiscoverySkipReason? Rejection)
{
    public bool Succeeded => Asset is not null;

    public static PngAssetResolution Rejected(PngDiscoverySkipReason reason) => new(null, reason);
}

internal static class PngAssetUrlResolver
{
    public const int MaxSafeRawValueLength = 512;
    public const int MaxDescriptorLength = 100;

    public static PngAssetResolution Resolve(
        string? rawUrl,
        Uri resolutionBase,
        CrawlUrlOptions urlOptions)
    {
        ArgumentNullException.ThrowIfNull(resolutionBase);
        ArgumentNullException.ThrowIfNull(urlOptions);
        var candidate = Clean(rawUrl);
        if (candidate.Length == 0)
        {
            return PngAssetResolution.Rejected(PngDiscoverySkipReason.MalformedUrl);
        }
        if (candidate.Length > EndpointUrlNormalizer.MaximumLength)
        {
            return PngAssetResolution.Rejected(PngDiscoverySkipReason.OverlongValue);
        }
        if (candidate.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return PngAssetResolution.Rejected(PngDiscoverySkipReason.DataUrl);
        }
        if (candidate.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
        {
            return PngAssetResolution.Rejected(PngDiscoverySkipReason.BlobUrl);
        }
        if (!TryBuildFetchUrl(candidate, resolutionBase, out var fetchUrl))
        {
            return PngAssetResolution.Rejected(PngDiscoverySkipReason.MalformedUrl);
        }
        if (fetchUrl.Length > EndpointUrlNormalizer.MaximumLength)
        {
            return PngAssetResolution.Rejected(PngDiscoverySkipReason.OverlongValue);
        }
        if (!Uri.TryCreate(fetchUrl, UriKind.Absolute, out var uri))
        {
            return PngAssetResolution.Rejected(PngDiscoverySkipReason.MalformedUrl);
        }
        if (!UrlTextNormalization.IsHttpScheme(uri))
        {
            return PngAssetResolution.Rejected(PngDiscoverySkipReason.UnsupportedScheme);
        }
        if (uri.UserInfo.Length > 0)
        {
            return PngAssetResolution.Rejected(PngDiscoverySkipReason.CredentialsPresent);
        }
        if (uri.Host.Length == 0 || uri.Host.Contains('%', StringComparison.Ordinal)
            || !UrlTextNormalization.HasReadableHost(uri))
        {
            return PngAssetResolution.Rejected(PngDiscoverySkipReason.MalformedUrl);
        }
        var requestIdentity = EndpointUrlNormalizer.Normalize(fetchUrl);
        if (!requestIdentity.Succeeded)
        {
            return PngAssetResolution.Rejected(PngDiscoverySkipReason.MalformedUrl);
        }

        var displayUrl = Bound(
            CrawlUrlRedactor.Redact(fetchUrl, urlOptions) ?? string.Empty,
            EndpointUrlNormalizer.MaximumLength);
        return new(new(
            fetchUrl,
            displayUrl,
            IdentityHash(requestIdentity.NormalizedUrl!),
            requestIdentity.NormalizedHost!), null);
    }

    public static Uri ResolutionBase(string documentUrl, string? baseHref)
    {
        var document = new Uri(documentUrl, UriKind.Absolute);
        var candidate = Clean(baseHref);
        if (candidate.Length == 0
            || !TryBuildFetchUrl(candidate, document, out var resolved)
            || !Uri.TryCreate(resolved, UriKind.Absolute, out var resolutionBase)
            || !UrlTextNormalization.IsHttpScheme(resolutionBase)
            || resolutionBase.UserInfo.Length > 0)
        {
            return document;
        }

        return resolutionBase;
    }

    public static string SafeRawValue(string? rawUrl, CrawlUrlOptions urlOptions) =>
        Bound(
            CrawlUrlRedactor.Redact(Clean(rawUrl), urlOptions) ?? string.Empty,
            MaxSafeRawValueLength);

    public static string IdentityHash(string url) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();

    public static string BoundedDescriptor(string? descriptor) =>
        Bound(Clean(descriptor), MaxDescriptorLength);

    private static bool TryBuildFetchUrl(string candidate, Uri resolutionBase, out string fetchUrl)
    {
        var fragmentStart = candidate.IndexOf('#', StringComparison.Ordinal);
        var withoutFragment = fragmentStart < 0 ? candidate : candidate[..fragmentStart];
        if (withoutFragment.StartsWith("//", StringComparison.Ordinal))
        {
            fetchUrl = $"{resolutionBase.Scheme}:{withoutFragment}";
            return true;
        }
        if (Uri.TryCreate(withoutFragment, UriKind.Absolute, out _))
        {
            fetchUrl = withoutFragment;
            return true;
        }

        var queryStart = withoutFragment.IndexOf('?', StringComparison.Ordinal);
        var path = queryStart < 0 ? withoutFragment : withoutFragment[..queryStart];
        var query = queryStart < 0 ? string.Empty : withoutFragment[queryStart..];
        var pathBase = new Uri(resolutionBase.GetLeftPart(UriPartial.Path), UriKind.Absolute);
        if (!Uri.TryCreate(pathBase, path, out var resolvedPath))
        {
            fetchUrl = string.Empty;
            return false;
        }

        fetchUrl = resolvedPath.GetLeftPart(UriPartial.Path) + query;
        return true;
    }

    private static string Clean(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string([.. value.Where(character => !char.IsControl(character))]).Trim();

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}

internal enum PngImageLedgerAdmission
{
    Accepted,
    UniqueImageLimit,
    SourceMappingLimit
}

internal sealed class PngImageLedger(PngImageDiscoveryLimits limits)
{
    private readonly Dictionary<string, PngDiscoveredImageRequest> _imagesByIdentity =
        new(StringComparer.Ordinal);
    private readonly HashSet<SourceMappingKey> _sourceMappingKeys = [];
    private readonly List<PngDiscoveredImageRequest> _images = [];
    private readonly List<PngImageSourceMapping> _sourceMappings = [];

    public IReadOnlyList<PngDiscoveredImageRequest> Images => _images;

    public IReadOnlyList<PngImageSourceMapping> SourceMappings => _sourceMappings;

    public PngImageLedgerAdmission Add(
        PngResolvedAsset asset,
        PngDiscoveredPage sourcePage,
        HtmlImageReference reference)
    {
        if (!_imagesByIdentity.TryGetValue(asset.IdentityHash, out var image))
        {
            if (_images.Count >= limits.MaxUniqueImages)
            {
                return PngImageLedgerAdmission.UniqueImageLimit;
            }

            image = new(asset.FetchUrl, asset.DisplayUrl, asset.IdentityHash);
            _imagesByIdentity.Add(asset.IdentityHash, image);
            _images.Add(image);
        }

        var descriptor = PngAssetUrlResolver.BoundedDescriptor(reference.Descriptor);
        var mappingKey = new SourceMappingKey(
            image.IdentityHash,
            sourcePage.IdentityHash,
            reference.AttributeKind,
            descriptor);
        if (!_sourceMappingKeys.Add(mappingKey))
        {
            return PngImageLedgerAdmission.Accepted;
        }
        if (_sourceMappings.Count >= limits.MaxTotalSourceMappings)
        {
            _sourceMappingKeys.Remove(mappingKey);
            return PngImageLedgerAdmission.SourceMappingLimit;
        }

        _sourceMappings.Add(new(
            image.IdentityHash,
            sourcePage.DisplayUrl,
            sourcePage.IdentityHash,
            reference.AttributeKind,
            descriptor.Length == 0 ? null : descriptor));
        return PngImageLedgerAdmission.Accepted;
    }

    private sealed record SourceMappingKey(
        string ImageIdentityHash,
        string SourcePageIdentityHash,
        string AttributeKind,
        string Descriptor);
}
