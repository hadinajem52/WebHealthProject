using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using WebHealth.Application.PngAudits;

namespace WebHealth.Infrastructure.PngAudits;

public sealed class PngImageAnalyzer : IPngImageAnalyzer, IDisposable
{
    private const int DecodedBytesPerPixel = 4;
    private const int HighBitDepthDecodedBytesPerPixel = 8;
    private readonly PngImageAnalysisLimits _limits;
    private readonly PngRecommendationThresholds _recommendationThresholds;
    private readonly IPngFormatComparisonEngine _comparisonEngine;
    private readonly DecoderOptions _decoderOptions;
    private readonly SemaphoreSlim _analysisGate = new(1, 1);
    private bool _isDisposed;

    public PngImageAnalyzer(
        PngImageAnalysisLimits limits,
        PngRecommendationThresholds recommendationThresholds,
        IPngFormatComparisonEngine comparisonEngine)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _recommendationThresholds = recommendationThresholds
            ?? throw new ArgumentNullException(nameof(recommendationThresholds));
        _comparisonEngine = comparisonEngine
            ?? throw new ArgumentNullException(nameof(comparisonEngine));
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
        CancellationToken cancellationToken = default) =>
        await AnalyzeAsync(
            encodedImage,
            _limits,
            _recommendationThresholds,
            cancellationToken);

    public async Task<PngAnalysisResult> AnalyzeAsync(
        ReadOnlyMemory<byte> encodedImage,
        PngImageAnalysisLimits limits,
        PngRecommendationThresholds recommendationThresholds,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(recommendationThresholds);
        cancellationToken.ThrowIfCancellationRequested();
        var originalBytes = encodedImage.Length;
        if (originalBytes == 0 || originalBytes > limits.MaxEncodedBytes)
        {
            return PngAnalysisResult.Failed(
                PngImageAnalysisClassification.IdentificationFailed,
                originalBytes);
        }

        var detectedFormat = DetectFormat(encodedImage.Span);
        if (detectedFormat is null)
        {
            return SvgContentDetector.IsSvg(encodedImage.Span)
                ? PngAnalysisResult.NotPng(originalBytes, SvgContentDetector.FormatName)
                : PngAnalysisResult.Failed(
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

        var boundedResult = ApplyResourceLimits(preflight, originalBytes, limits);
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
                recommendationThresholds,
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

    private static PngAnalysisResult? ApplyResourceLimits(
        PngChunkPreflight preflight,
        long originalBytes,
        PngImageAnalysisLimits limits)
    {
        if (preflight.Width > limits.MaxWidth || preflight.Height > limits.MaxHeight)
        {
            return PngAnalysisResult.Failed(
                PngImageAnalysisClassification.DimensionsExceeded,
                originalBytes);
        }

        if (preflight.PixelCount > limits.MaxDecodedPixels)
        {
            return PngAnalysisResult.Failed(
                PngImageAnalysisClassification.PixelLimitExceeded,
                originalBytes);
        }

        var bytesPerPixel = preflight.IsHighBitDepth
            ? HighBitDepthDecodedBytesPerPixel
            : DecodedBytesPerPixel;
        if (preflight.PixelCount > limits.MaxDecodedMemoryBytes / bytesPerPixel)
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
        PngRecommendationThresholds recommendationThresholds,
        CancellationToken cancellationToken)
    {
        var originalBytes = encodedImage.Length;
        if (preflight.IsHighBitDepth)
        {
            using var wide = await DecodeAsync<Rgba64>(encodedImage, cancellationToken);
            if (wide is null || wide.Width != preflight.Width || wide.Height != preflight.Height)
            {
                return PngAnalysisResult.Failed(
                    PngImageAnalysisClassification.DecodeFailed,
                    originalBytes);
            }

            var wideTransparency = MeasureWideTransparency(wide, cancellationToken);
            return PngAnalysisResult.HighBitDepth(
                originalBytes,
                preflight.CreateFacts(wideTransparency));
        }

        PngTransparencyFacts transparency;
        using (var image = await DecodeAsync<Rgba32>(encodedImage, cancellationToken))
        {
            if (image is null || image.Width != preflight.Width || image.Height != preflight.Height)
            {
                return PngAnalysisResult.Failed(
                    PngImageAnalysisClassification.DecodeFailed,
                    originalBytes);
            }

            transparency = MeasureTransparency(image, cancellationToken);
        }

        var facts = preflight.CreateFacts(transparency);
        if (!preflight.CanTransferColorMeaning)
        {
            return PngAnalysisResult.ColorProfileUnsupported(originalBytes, facts);
        }

        var comparison = await _comparisonEngine.CompareAsync(
            encodedImage,
            preflight.SourceEncodingFacts,
            recommendationThresholds,
            cancellationToken);
        if (comparison.VerifiedWebpBytes is not { } verifiedWebpBytes)
        {
            return PngAnalysisResult.ComparisonUnavailable(
                originalBytes,
                facts,
                unavailableReason: comparison.UnavailableReason!);
        }
        if (comparison.UnavailableReason is not null)
        {
            return PngAnalysisResult.ComparisonUnavailable(
                originalBytes,
                facts,
                new PngComparisonMetrics(originalBytes, verifiedWebpBytes),
                comparison.UnavailableReason);
        }
        return comparison.VerifiedOptimizedPngBytes is { } optimizedPngBytes
            ? PngAnalysisResult.ComparedAgainstReference(
                originalBytes,
                facts,
                verifiedWebpBytes,
                optimizedPngBytes,
                recommendationThresholds)
            : PngAnalysisResult.BelowWebpThreshold(
                originalBytes,
                facts,
                verifiedWebpBytes);
    }

    private async Task<Image<TPixel>?> DecodeAsync<TPixel>(
        ReadOnlyMemory<byte> encodedImage,
        CancellationToken cancellationToken)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        try
        {
            await using var stream = new MemoryStream(encodedImage.ToArray(), writable: false);
            return await Image.LoadAsync<TPixel>(_decoderOptions, stream, cancellationToken);
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

    private static PngTransparencyFacts MeasureTransparency(
        Image<Rgba32> image,
        CancellationToken cancellationToken)
    {
        var width = image.Width;
        var height = image.Height;
        long pixelCount = (long)width * height;
        long semiTransparent = 0;
        long fullyTransparent = 0;
        var minAlpha = byte.MaxValue;
        byte[]? fullyTransparentMap = null;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var alpha = row[x].A;
                    if (alpha == byte.MaxValue)
                    {
                        continue;
                    }
                    if (alpha < minAlpha)
                    {
                        minAlpha = alpha;
                    }
                    if (alpha == 0)
                    {
                        fullyTransparent++;
                        fullyTransparentMap ??= new byte[pixelCount];
                        fullyTransparentMap[(y * (long)width) + x] = 1;
                    }
                    else
                    {
                        semiTransparent++;
                    }
                }
            }
        });

        var background = fullyTransparentMap is null
            ? 0
            : CountBorderConnected(fullyTransparentMap, width, height, cancellationToken);
        return new(
            pixelCount,
            semiTransparent,
            fullyTransparent,
            background,
            fullyTransparent - background,
            minAlpha);
    }

    private static PngTransparencyFacts MeasureWideTransparency(
        Image<Rgba64> image,
        CancellationToken cancellationToken)
    {
        var width = image.Width;
        var height = image.Height;
        long pixelCount = (long)width * height;
        long semiTransparent = 0;
        long fullyTransparent = 0;
        var minAlpha = ushort.MaxValue;
        byte[]? fullyTransparentMap = null;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var alpha = row[x].A;
                    if (alpha == ushort.MaxValue)
                    {
                        continue;
                    }
                    if (alpha < minAlpha)
                    {
                        minAlpha = alpha;
                    }
                    if (alpha == 0)
                    {
                        fullyTransparent++;
                        fullyTransparentMap ??= new byte[pixelCount];
                        fullyTransparentMap[(y * (long)width) + x] = 1;
                    }
                    else
                    {
                        semiTransparent++;
                    }
                }
            }
        });

        var background = fullyTransparentMap is null
            ? 0
            : CountBorderConnected(fullyTransparentMap, width, height, cancellationToken);
        return new(
            pixelCount,
            semiTransparent,
            fullyTransparent,
            background,
            fullyTransparent - background,
            NarrowAlpha(minAlpha));
    }

    private static byte NarrowAlpha(ushort alpha) =>
        alpha == ushort.MaxValue
            ? byte.MaxValue
            : Math.Min((byte)(byte.MaxValue - 1), (byte)(alpha >> 8));

    private static long CountBorderConnected(
        byte[] fullyTransparentMap,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var pending = new Queue<int>();
        void Offer(int x, int y)
        {
            var index = (y * width) + x;
            if (fullyTransparentMap[index] == 1)
            {
                fullyTransparentMap[index] = 2;
                pending.Enqueue(index);
            }
        }

        for (var x = 0; x < width; x++)
        {
            Offer(x, 0);
            Offer(x, height - 1);
        }
        for (var y = 0; y < height; y++)
        {
            Offer(0, y);
            Offer(width - 1, y);
        }

        long connected = 0;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = pending.Dequeue();
            connected++;
            var x = index % width;
            var y = index / width;
            if (x > 0) Offer(x - 1, y);
            if (x < width - 1) Offer(x + 1, y);
            if (y > 0) Offer(x, y - 1);
            if (y < height - 1) Offer(x, y + 1);
        }

        return connected;
    }

}
