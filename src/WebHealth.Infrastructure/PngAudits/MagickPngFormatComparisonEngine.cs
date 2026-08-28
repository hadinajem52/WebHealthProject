using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ImageMagick;
using ImageMagick.Formats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using WebHealth.Application.PngAudits;

namespace WebHealth.Infrastructure.PngAudits;

internal sealed class MagickPngFormatComparisonEngine : IPngFormatComparisonEngine
{
    private const string EncodingFailed = "EncodingFailed";
    private const string FidelityVerificationFailed = "FidelityVerificationFailed";
    private const string ColorProfileVerificationFailed = "ColorProfileVerificationFailed";
    private const string InvalidWebpPayload = "InvalidWebpPayload";
    private const string OriginalTooSmallForThreshold = "OriginalTooSmallForThreshold";
    private const string OutputLimitExceeded = "OutputLimitExceeded";
    private const string OptimizerUnavailable = "OptimizerUnavailable";
    private const string OptimizerFailed = "OptimizerFailed";
    private const string OptimizerTimedOut = "OptimizerTimedOut";
    private const string OptimizedPngOutputLimitExceeded = "OptimizedPngOutputLimitExceeded";
    private const string OptimizedPngFidelityVerificationFailed = "OptimizedPngFidelityVerificationFailed";
    private const string OptimizedPngColorProfileVerificationFailed = "OptimizedPngColorProfileVerificationFailed";
    private static readonly SemaphoreSlim OptimizerGate = new(1, 1);
    private readonly int _maxOutputBytes;
    private readonly TimeSpan _timeout;
    private readonly DecoderOptions _decoderOptions;

    public MagickPngFormatComparisonEngine(PngAuditOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _maxOutputBytes = options.MaxImageBytes;
        _timeout = TimeSpan.FromSeconds(options.ComparisonTimeoutSeconds);
        _decoderOptions = new DecoderOptions
        {
            Configuration = Configuration.Default.Clone(),
            MaxFrames = 1,
            SkipMetadata = true
        };
        _decoderOptions.Configuration.MaxDegreeOfParallelism = 1;
        ResourceLimits.Width = (ulong)options.MaxWidth;
        ResourceLimits.Height = (ulong)options.MaxHeight;
        ResourceLimits.Area = (ulong)options.MaxDecodedPixels;
        ResourceLimits.Memory = (ulong)options.MaxDecodedMemoryBytes;
        ResourceLimits.Disk = 0;
        ResourceLimits.Thread = 1;
        ResourceLimits.Time = (ulong)options.ComparisonTimeoutSeconds;
    }

    public async Task<PngFormatComparisonResult> CompareAsync(
        ReadOnlyMemory<byte> sourcePng,
        PngSourceEncodingFacts sourceFacts,
        PngRecommendationThresholds thresholds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceFacts);
        ArgumentNullException.ThrowIfNull(thresholds);
        if (!CanMeetThreshold(sourcePng.Length, thresholds))
        {
            return Unavailable(OriginalTooSmallForThreshold);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var optimizing = false;
        ReadOnlyMemory<byte> encoded = default;
        try
        {
            encoded = await EncodeAsync(sourcePng, _maxOutputBytes, timeout.Token);
            if (!WebpContainer.TryInspect(encoded.Span, out var hasLosslessPayload, out var iccProfile)
                || !hasLosslessPayload)
            {
                return Unavailable(InvalidWebpPayload);
            }
            if (!await HasExactPixelsAsync(sourcePng, encoded, timeout.Token))
            {
                return Unavailable(FidelityVerificationFailed);
            }
            if (!HasExpectedColorProfile(sourcePng.Span, sourceFacts, iccProfile))
            {
                return Unavailable(ColorProfileVerificationFailed);
            }

            if (!thresholds.MeetsThreshold(sourcePng.Length, encoded.Length))
            {
                return PngFormatComparisonResult.WebpOnly(
                    encoded.Length,
                    PngAnalysisProfiles.VerifiedComparison);
            }

            optimizing = true;
            var optimized = await OptimizePngAsync(sourcePng, timeout.Token);
            if (optimized.Bytes is null)
            {
                return PngFormatComparisonResult.PartialUnavailable(
                    encoded.Length,
                    optimized.UnavailableReason!,
                    PngAnalysisProfiles.VerifiedComparison);
            }
            if (!await HasExactPixelsAsync(sourcePng, optimized.Bytes.Value, timeout.Token))
            {
                return PngFormatComparisonResult.PartialUnavailable(
                    encoded.Length,
                    OptimizedPngFidelityVerificationFailed,
                    PngAnalysisProfiles.VerifiedComparison);
            }
            if (!HasExpectedOptimizedPngColorProfile(
                optimized.Bytes.Value.Span,
                sourcePng.Span,
                sourceFacts))
            {
                return PngFormatComparisonResult.PartialUnavailable(
                    encoded.Length,
                    OptimizedPngColorProfileVerificationFailed,
                    PngAnalysisProfiles.VerifiedComparison);
            }

            return PngFormatComparisonResult.Complete(
                encoded.Length,
                optimized.Bytes.Value.Length,
                PngAnalysisProfiles.VerifiedComparison);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return optimizing && !encoded.IsEmpty
                ? PngFormatComparisonResult.PartialUnavailable(
                    encoded.Length,
                    OptimizerTimedOut,
                    PngAnalysisProfiles.VerifiedComparison)
                : Unavailable(EncodingFailed);
        }
        catch (OutputLimitExceededException)
        {
            return Unavailable(OutputLimitExceeded);
        }
        catch (MagickException)
        {
            return Unavailable(EncodingFailed);
        }
        catch (InvalidImageContentException)
        {
            return Unavailable(FidelityVerificationFailed);
        }
        catch (UnknownImageFormatException)
        {
            return Unavailable(FidelityVerificationFailed);
        }
        catch (NotSupportedException)
        {
            return Unavailable(FidelityVerificationFailed);
        }
    }

    private async Task<OptimizedPngResult> OptimizePngAsync(
        ReadOnlyMemory<byte> sourcePng,
        CancellationToken cancellationToken)
    {
        var executable = ResolveOptimizerPath();
        if (executable is null)
        {
            return new(null, OptimizerUnavailable);
        }

        await OptimizerGate.WaitAsync(cancellationToken);
        var directory = Path.Combine(Path.GetTempPath(), $"webhealth-oxipng-{Guid.NewGuid():N}");
        var inputPath = Path.Combine(directory, "input.png");
        var outputPath = Path.Combine(directory, "optimized.png");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(inputPath, sourcePng, cancellationToken);
            using var process = new Process
            {
                StartInfo = CreateOptimizerStartInfo(executable, inputPath, outputPath)
            };
            if (!process.Start())
            {
                return new(null, OptimizerFailed);
            }
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
                throw;
            }

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                return new(null, OptimizerFailed);
            }
            var length = new FileInfo(outputPath).Length;
            if (length <= 0)
            {
                return new(null, OptimizerFailed);
            }
            if (length > _maxOutputBytes)
            {
                return new(null, OptimizedPngOutputLimitExceeded);
            }

            return new(await File.ReadAllBytesAsync(outputPath, cancellationToken), null);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new(null, OptimizerUnavailable);
        }
        catch (IOException)
        {
            return new(null, OptimizerFailed);
        }
        catch (UnauthorizedAccessException)
        {
            return new(null, OptimizerUnavailable);
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            OptimizerGate.Release();
        }
    }

    private ProcessStartInfo CreateOptimizerStartInfo(
        string executable,
        string inputPath,
        string outputPath)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("max");
        startInfo.ArgumentList.Add("--strip");
        startInfo.ArgumentList.Add("safe");
        startInfo.ArgumentList.Add("--quiet");
        startInfo.ArgumentList.Add("--timeout");
        startInfo.ArgumentList.Add(Math.Max(1, (int)_timeout.TotalSeconds).ToString());
        startInfo.ArgumentList.Add("--out");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add(inputPath);
        return startInfo;
    }

    private static string? ResolveOptimizerPath()
    {
        var relativePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? Path.Combine("Tools", "Oxipng", "win-x64", "oxipng.exe")
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                && RuntimeInformation.ProcessArchitecture == Architecture.X64
                ? Path.Combine("Tools", "Oxipng", "linux-x64", "oxipng")
                : null;
        if (relativePath is null)
        {
            return null;
        }

        var path = Path.Combine(AppContext.BaseDirectory, relativePath);
        return File.Exists(path) ? path : null;
    }

    private static bool CanMeetThreshold(
        int originalBytes,
        PngRecommendationThresholds thresholds) =>
        originalBytes > thresholds.MinSavingsBytes
        && thresholds.MeetsThreshold(originalBytes, 1);

    internal static async Task<ReadOnlyMemory<byte>> EncodeAsync(
        ReadOnlyMemory<byte> sourcePng,
        int maxOutputBytes,
        CancellationToken cancellationToken)
    {
        using var image = new MagickImage(sourcePng.Span);
        image.RemoveProfile("exif");
        image.RemoveProfile("xmp");
        image.Quality = 100;
        var defines = new WebPWriteDefines
        {
            Exact = true,
            Lossless = true,
            Method = 6,
            ThreadLevel = false
        };
        await using var output = new BoundedMemoryStream(maxOutputBytes);
        await image.WriteAsync(output, defines, cancellationToken);
        if (output.LimitExceeded)
        {
            throw new OutputLimitExceededException();
        }
        return output.WrittenMemory;
    }

    private async Task<bool> HasExactPixelsAsync(
        ReadOnlyMemory<byte> sourcePng,
        ReadOnlyMemory<byte> candidateWebp,
        CancellationToken cancellationToken)
    {
        using var source = await LoadRgbaAsync(sourcePng, cancellationToken);
        using var candidate = await LoadRgbaAsync(candidateWebp, cancellationToken);
        if (source.Width != candidate.Width || source.Height != candidate.Height)
        {
            return false;
        }

        var matches = true;
        source.ProcessPixelRows(candidate, (sourceRows, candidateRows) =>
        {
            for (var y = 0; y < sourceRows.Height && matches; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                matches = sourceRows.GetRowSpan(y).SequenceEqual(candidateRows.GetRowSpan(y));
            }
        });
        return matches;
    }

    private async Task<Image<Rgba32>> LoadRgbaAsync(
        ReadOnlyMemory<byte> encoded,
        CancellationToken cancellationToken)
    {
        await using var input = new MemoryStream(encoded.ToArray(), writable: false);
        return await Image.LoadAsync<Rgba32>(_decoderOptions, input, cancellationToken);
    }

    private static bool HasExpectedColorProfile(
        ReadOnlySpan<byte> sourcePng,
        PngSourceEncodingFacts sourceFacts,
        byte[]? candidateIccProfile)
    {
        if (sourceFacts.ColorMeaning != PngColorMeaning.IccProfile)
        {
            return candidateIccProfile is null;
        }

        using var source = new MagickImage(sourcePng);
        var sourceProfile = (source.GetProfile("icc") ?? source.GetProfile("icm"))?.ToByteArray();
        return sourceProfile is not null
            && candidateIccProfile is not null
            && sourceProfile.AsSpan().SequenceEqual(candidateIccProfile);
    }

    private static bool HasExpectedOptimizedPngColorProfile(
        ReadOnlySpan<byte> candidatePng,
        ReadOnlySpan<byte> sourcePng,
        PngSourceEncodingFacts sourceFacts)
    {
        if (!PngChunkInspector.TryInspect(candidatePng, out var candidateFacts)
            || candidateFacts.SourceEncodingFacts.ColorMeaning != sourceFacts.ColorMeaning)
        {
            return false;
        }
        if (sourceFacts.ColorMeaning != PngColorMeaning.IccProfile)
        {
            return true;
        }

        using var source = new MagickImage(sourcePng);
        using var candidate = new MagickImage(candidatePng);
        var sourceProfile = (source.GetProfile("icc") ?? source.GetProfile("icm"))?.ToByteArray();
        var candidateProfile = (candidate.GetProfile("icc") ?? candidate.GetProfile("icm"))?.ToByteArray();
        return sourceProfile is not null
            && candidateProfile is not null
            && sourceProfile.AsSpan().SequenceEqual(candidateProfile);
    }

    private static PngFormatComparisonResult Unavailable(string reason) =>
        PngFormatComparisonResult.Unavailable(reason, PngAnalysisProfiles.VerifiedComparison);
}

internal sealed record OptimizedPngResult(ReadOnlyMemory<byte>? Bytes, string? UnavailableReason);

internal sealed class BoundedMemoryStream : MemoryStream
{
    private readonly int _maxLength;

    public BoundedMemoryStream(int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        _maxLength = maxLength;
    }

    public ReadOnlyMemory<byte> WrittenMemory =>
        GetBuffer().AsMemory(0, checked((int)Length));

    public bool LimitExceeded { get; private set; }

    public override void SetLength(long value)
    {
        if (value > _maxLength)
        {
            LimitExceeded = true;
            value = _maxLength;
        }
        base.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var accepted = AcceptedCount(count);
        base.Write(buffer, offset, accepted);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        base.Write(buffer[..AcceptedCount(buffer.Length)]);
    }

    public override void WriteByte(byte value)
    {
        if (AcceptedCount(1) == 1)
        {
            base.WriteByte(value);
        }
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        return base.WriteAsync(buffer, offset, AcceptedCount(count), cancellationToken);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        return base.WriteAsync(buffer[..AcceptedCount(buffer.Length)], cancellationToken);
    }

    private int AcceptedCount(int requestedCount)
    {
        var available = Math.Max(0, _maxLength - Position);
        if (requestedCount <= available)
        {
            return requestedCount;
        }

        LimitExceeded = true;
        return checked((int)available);
    }
}

internal sealed class OutputLimitExceededException : IOException;

internal static class WebpContainer
{
    public static bool TryInspect(
        ReadOnlySpan<byte> encoded,
        out bool hasLosslessPayload,
        out byte[]? iccProfile)
    {
        hasLosslessPayload = false;
        iccProfile = null;
        if (encoded.Length < 12
            || !encoded[..4].SequenceEqual("RIFF"u8)
            || !encoded.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return false;
        }

        var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(encoded.Slice(4, 4));
        if (declaredLength != encoded.Length - 8)
        {
            return false;
        }

        var offset = 12;
        while (offset <= encoded.Length - 8)
        {
            var type = encoded.Slice(offset, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(encoded.Slice(offset + 4, 4));
            if (length > int.MaxValue)
            {
                return false;
            }
            var dataOffset = offset + 8;
            var paddedLength = checked((int)length + ((int)length & 1));
            if (dataOffset > encoded.Length - paddedLength)
            {
                return false;
            }
            if (type.SequenceEqual("VP8L"u8))
            {
                hasLosslessPayload = true;
            }
            else if (type.SequenceEqual("ICCP"u8))
            {
                iccProfile = encoded.Slice(dataOffset, (int)length).ToArray();
            }
            offset = dataOffset + paddedLength;
        }

        return offset == encoded.Length;
    }
}
