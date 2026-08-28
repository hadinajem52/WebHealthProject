using WebHealth.Infrastructure.Registry;

namespace WebHealth.Infrastructure.PngAudits;

public sealed class PngAuditRun
{
    public Guid Id { get; set; }
    public Guid EndpointId { get; set; }
    public required string Source { get; set; }
    public Guid? InitiatedByUserId { get; set; }
    public required string Status { get; set; }
    public string? FailureCode { get; set; }
    public string? SafeDiagnostic { get; set; }

    public required string SeedUrlSnapshot { get; set; }
    public required byte[] SeedUrlIdentityHash { get; set; }
    public bool IsProductionSnapshot { get; set; }
    public required string AllowedPageHosts { get; set; }
    public required string AllowedPagePathPrefixes { get; set; }
    public required string AllowedAssetHosts { get; set; }
    public required string QueryPolicy { get; set; }
    public required string TrackingQueryParameters { get; set; }
    public required string SensitiveQueryParameters { get; set; }
    public int MaxQueryParameters { get; set; }

    public int MaxPages { get; set; }
    public int MaxDepth { get; set; }
    public int MaxPageBytes { get; set; }
    public long MaxTotalPageBytes { get; set; }
    public int MaxImageReferencesPerPage { get; set; }
    public int MaxUniqueImages { get; set; }
    public int MaxTotalImageSourceMappings { get; set; }
    public int MaxImageBytes { get; set; }
    public long MaxTotalImageBytes { get; set; }
    public int MaxWidth { get; set; }
    public int MaxHeight { get; set; }
    public long MaxDecodedPixels { get; set; }
    public long MaxDecodedMemoryBytes { get; set; }
    public int MaxTotalHttpAttempts { get; set; }
    public int FetchTimeoutSeconds { get; set; }
    public double RequestsPerSecondPerHost { get; set; }
    public int TransientRetryCount { get; set; }
    public int MaxDurationSeconds { get; set; }
    public decimal MinSavingsPercent { get; set; }
    public long MinSavingsBytes { get; set; }
    public required string AnalyzerProfile { get; set; }
    public required string ComparisonProfile { get; set; }

    public int PagesDiscovered { get; set; }
    public int ImagesDiscovered { get; set; }
    public int ImagesAnalyzed { get; set; }
    public int RecommendationCount { get; set; }
    public int DiscoverySkipCount { get; set; }
    public int HttpAttempts { get; set; }
    public long TotalPageBytes { get; set; }
    public long TotalImageBytes { get; set; }
    public bool CrawlCoverageLimited { get; set; }
    public bool ImageAnalysisCoverageLimited { get; set; }
    public bool SourceMappingCoverageLimited { get; set; }

    public int AttemptCount { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public Endpoint Endpoint { get; set; } = null!;
    public ICollection<PngAuditImageResult> ImageResults { get; } = [];
    public ICollection<PngAuditDiscoverySkipEntity> DiscoverySkips { get; } = [];
    public ICollection<PngAuditCoverageReasonEntity> CoverageReasons { get; } = [];
}

public sealed class PngAuditImageResult
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public required string ImageDisplayUrl { get; set; }
    public required byte[] ImageIdentityHash { get; set; }
    public string? FinalDisplayUrl { get; set; }
    public byte[]? FinalIdentityHash { get; set; }
    public string? DeclaredContentType { get; set; }
    public string? DetectedFormat { get; set; }
    public int? HttpStatusCode { get; set; }
    public long ResponseBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? FrameCount { get; set; }
    public long? PixelCount { get; set; }
    public int? BitDepth { get; set; }
    public int? ColorType { get; set; }
    public bool? UsesTransparency { get; set; }
    public long? TransparentPixelCount { get; set; }
    public decimal? TransparentPixelPercent { get; set; }
    public long? SemiTransparentPixelCount { get; set; }
    public long? FullyTransparentPixelCount { get; set; }
    public long? BackgroundTransparentPixelCount { get; set; }
    public long? InteriorTransparentPixelCount { get; set; }
    public int? MinAlpha { get; set; }
    public required string Classification { get; set; }
    public string? ReasonCode { get; set; }
    public required string Recommendation { get; set; }
    public string? SuggestedFormat { get; set; }
    public long? OptimizedPngBytes { get; set; }
    public long? CandidateWebpBytes { get; set; }
    public long? OriginalSavingsBytes { get; set; }
    public decimal? OriginalSavingsPercent { get; set; }
    public long? ReferenceSavingsBytes { get; set; }
    public decimal? ReferenceSavingsPercent { get; set; }
    public DateTimeOffset RecordedAt { get; set; }

    public PngAuditRun Run { get; set; } = null!;
    public ICollection<PngAuditImageSource> Sources { get; } = [];
}

public sealed class PngAuditImageSource
{
    public Guid Id { get; set; }
    public Guid ImageResultId { get; set; }
    public required string SourcePageDisplayUrl { get; set; }
    public required byte[] SourcePageIdentityHash { get; set; }
    public required string AttributeKind { get; set; }
    public string? Descriptor { get; set; }

    public PngAuditImageResult ImageResult { get; set; } = null!;
}

public sealed class PngAuditDiscoverySkipEntity
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public required string SourcePageDisplayUrl { get; set; }
    public required byte[] SourcePageIdentityHash { get; set; }
    public required string AttributeKind { get; set; }
    public string? Descriptor { get; set; }
    public required string BoundedSafeRawValue { get; set; }
    public required string ReasonCode { get; set; }
    public DateTimeOffset RecordedAt { get; set; }

    public PngAuditRun Run { get; set; } = null!;
}

public sealed class PngAuditCoverageReasonEntity
{
    public Guid RunId { get; set; }
    public required string Area { get; set; }
    public required string ReasonCode { get; set; }
    public int Count { get; set; }

    public PngAuditRun Run { get; set; } = null!;
}
