using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using WebHealth.Application.PngAudits;

namespace WebHealth.Infrastructure.PngAudits;

public sealed class PngImageAnalyzer : IPngImageAnalyzer, IDisposable
{
    private const int DecodedBytesPerPixel = 4;
    private readonly PngImageAnalysisLimits _limits;
    private readonly PngRecommendationThresholds _recommendationThresholds;
    private readonly DecoderOptions _decoderOptions;
    private readonly SemaphoreSlim _analysisGate = new(1, 1);
    private bool _isDisposed;

    public PngImageAnalyzer(
        PngImageAnalysisLimits limits,
        PngRecommendationThresholds recommendationThresholds)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _recommendationThresholds = recommendationThresholds
            ?? throw new ArgumentNullException(nameof(recommendationThresholds));
        var configuration = Configuration.Default.Clone();
        configuration.MaxDegreeOfParallelism = 1;
        _decoderOptions = new DecoderOptions
        {
            Configuration = configuration,
            MaxFrames = 1,
            SkipMetadata = true
        };
    }

    public async Task<PngAnalysisResult> AnalyzeAsync(
        ReadOnlyMemory<byte> encodedImage,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var originalBytes = encodedImage.Length;
        if (originalBytes == 0 || originalBytes > _limits.MaxEncodedBytes)
        {
            return PngAnalysisResult.Failed(
                PngImageAnalysisClassification.IdentificationFailed,
                originalBytes);
        }

        var detectedFormat = DetectFormat(encodedImage.Span);
        if (detectedFormat is null)
        {
            return PngAnalysisResult.Failed(
                PngImageAnalysisClassification.IdentificationFailed,
                originalBytes);
        }

        if (!string.Equals(detectedFormat.Name, "PNG", StringComparison.OrdinalIgnoreCase))
        {
            return PngAnalysisResult.NotPng(originalBytes, detectedFormat.Name);
        }

        if (!PngChunkInspector.TryInspect(encodedImage.Span, out var preflight))
        {
            return PngAnalysisResult.Failed(
                PngImageAnalysisClassification.IdentificationFailed,
                originalBytes);
        }

        var boundedResult = ApplyResourceLimits(preflight, originalBytes);
        if (boundedResult is not null)
        {
            return boundedResult;
        }

        await _analysisGate.WaitAsync(cancellationToken);
        try
        {
            return await AnalyzeDecodedImageAsync(
                encodedImage,
                preflight,
                cancellationToken);
        }
        finally
        {
            _analysisGate.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _analysisGate.Dispose();
        _isDisposed = true;
    }

    private static IImageFormat? DetectFormat(ReadOnlySpan<byte> encoded)
    {
        try
        {
            return Image.DetectFormat(encoded);
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private PngAnalysisResult? ApplyResourceLimits(
        PngChunkPreflight preflight,
        long originalBytes)
    {
        if (preflight.Width > _limits.MaxWidth || preflight.Height > _limits.MaxHeight)
        {
            return PngAnalysisResult.Failed(
                PngImageAnalysisClassification.DimensionsExceeded,
                originalBytes);
        }

        if (preflight.PixelCount > _limits.MaxDecodedPixels)
        {
            return PngAnalysisResult.Failed(
                PngImageAnalysisClassification.PixelLimitExceeded,
                originalBytes);
        }

        if (!preflight.HasSupportedBitDepth)
        {
            return PngAnalysisResult.Failed(
                PngImageAnalysisClassification.UnsupportedBitDepth,
                originalBytes);
        }

        if (preflight.PixelCount > _limits.MaxDecodedMemoryBytes / DecodedBytesPerPixel)
        {
            return PngAnalysisResult.Failed(
                PngImageAnalysisClassification.DecodedMemoryExceeded,
                originalBytes);
        }

        return preflight.FrameCount > 1
            ? PngAnalysisResult.Animated(originalBytes, preflight.CreateFacts())
            : null;
    }

    private async Task<PngAnalysisResult> AnalyzeDecodedImageAsync(
        ReadOnlyMemory<byte> encodedImage,
        PngChunkPreflight preflight,
        CancellationToken cancellationToken)
    {
        using var image = await DecodeAsync(encodedImage, cancellationToken);
        if (image is null || image.Width != preflight.Width || image.Height != preflight.Height)
        {
            return PngAnalysisResult.Failed(
                PngImageAnalysisClassification.DecodeFailed,
                encodedImage.Length);
        }

        var transparentPixelCount = CountTransparentPixels(image, cancellationToken);
        var imageFacts = preflight.CreateFacts(transparentPixelCount);
        if (imageFacts.UsesTransparency is true)
        {
            return PngAnalysisResult.Transparent(encodedImage.Length, imageFacts);
        }

        return await CompareEncodingsAsync(
            image,
            imageFacts,
            encodedImage.Length,
            cancellationToken);
    }

    private async Task<Image<Rgba32>?> DecodeAsync(
        ReadOnlyMemory<byte> encodedImage,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new MemoryStream(encodedImage.ToArray(), writable: false);
            return await Image.LoadAsync<Rgba32>(_decoderOptions, stream, cancellationToken);
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static long CountTransparentPixels(
        Image<Rgba32> image,
        CancellationToken cancellationToken)
    {
        long transparentPixelCount = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var pixel in accessor.GetRowSpan(y))
                {
                    transparentPixelCount += pixel.A < byte.MaxValue ? 1 : 0;
                }
            }
        });
        return transparentPixelCount;
    }

    private async Task<PngAnalysisResult> CompareEncodingsAsync(
        Image<Rgba32> image,
        PngImageFacts imageFacts,
        long originalBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            var normalizedPngBytes = await CountNormalizedPngBytesAsync(image, cancellationToken);
            var losslessWebpBytes = await CountLosslessWebpBytesAsync(image, cancellationToken);
            var comparison = new PngComparisonMetrics(
                originalBytes,
                normalizedPngBytes,
                losslessWebpBytes);
            return PngAnalysisResult.Compared(
                imageFacts,
                comparison,
                _recommendationThresholds);
        }
        catch (InvalidImageContentException)
        {
            return PngAnalysisResult.ComparisonFailed(originalBytes, imageFacts);
        }
        catch (NotSupportedException)
        {
            return PngAnalysisResult.ComparisonFailed(originalBytes, imageFacts);
        }
    }

    private static async Task<long> CountNormalizedPngBytesAsync(
        Image<Rgba32> image,
        CancellationToken cancellationToken)
    {
        await using var output = new CountingStream();
        await image.SaveAsPngAsync(output, new PngEncoder
        {
            BitDepth = PngBitDepth.Bit8,
            ColorType = PngColorType.Rgb,
            CompressionLevel = PngCompressionLevel.BestCompression,
            FilterMethod = PngFilterMethod.Adaptive,
            SkipMetadata = true
        }, cancellationToken);
        return output.Count;
    }

    private static async Task<long> CountLosslessWebpBytesAsync(
        Image<Rgba32> image,
        CancellationToken cancellationToken)
    {
        await using var output = new CountingStream();
        await image.SaveAsWebpAsync(output, new WebpEncoder
        {
            FileFormat = WebpFileFormatType.Lossless,
            Method = WebpEncodingMethod.BestQuality,
            Quality = 100,
            SkipMetadata = true
        }, cancellationToken);
        return output.Count;
    }
}
