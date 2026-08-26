using System.Net;

namespace WebHealth.Application.Monitoring;

public interface ISafeHttpTransport
{
    Task<SafeHttpTransportResult> SendAsync(
        SafeHttpTransportRequest request,
        CancellationToken cancellationToken = default);
}

public interface IMonitoringDnsResolver
{
    ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken = default);
}

public interface IDestinationAddressPolicy
{
    bool IsAllowed(IPAddress address);
}

public sealed record SafeHttpTransportRequest(
    Guid EndpointId,
    string Url,
    bool IsProduction,
    int MaxRedirects = SafeHttpTransportDefaults.MaxRedirects,
    int MaxResponseBodyBytes = SafeHttpTransportDefaults.DefaultMaxResponseBodyBytes,
    int TimeoutSeconds = SafeHttpTransportDefaults.DefaultTimeoutSeconds)
{
    public ISafeHttpRequestHopPolicy? HopPolicy { get; init; }
}

public sealed record SafeHttpRequestHop(string Url, int RedirectCount);

public interface ISafeHttpRequestHopPolicy
{
    Task<SafeHttpRequestHopDecision> EvaluateAsync(
        SafeHttpRequestHop hop,
        CancellationToken cancellationToken = default);
}

public sealed record SafeHttpRequestHopDecision(bool Allowed, string? RejectionReason = null);

public sealed record SafeHttpTransportResult(
    SafeHttpFailureKind? Failure,
    int? StatusCode,
    SafeHttpDestination? FinalDestination,
    TimeSpan Duration,
    long ResponseBytesRead,
    bool BodyTruncated,
    ReadOnlyMemory<byte> Body,
    IReadOnlyList<SafeHttpRedirectHop> Redirects,
    string? RequestIdentity = null,
    SafeHttpPhaseTiming? Timing = null,
    TlsCertificateObservation? Certificate = null,
    long? TransferredLength = null,
    string? ContentType = null,
    TimeSpan? RetryAfter = null,
    string? PolicyRejectionReason = null)
{
    public bool Succeeded => Failure is null;

    public string? FinalRequestUrl { get; init; }

    public int OutboundRequestCount { get; init; } =
        Failure is SafeHttpFailureKind.InvalidUrl or SafeHttpFailureKind.DestinationRejected
            ? 0
            : Failure == SafeHttpFailureKind.RequestPolicyRejected
                ? Redirects.Count
                : Redirects.Count + 1;
}

public sealed record SafeHttpPhaseTiming(
    int? DnsDurationMs,
    int? ConnectDurationMs,
    int? TlsDurationMs,
    int? TtfbDurationMs);

public sealed record SafeHttpRedirectHop(
    int StatusCode,
    string FromUrl,
    string ToUrl,
    bool IsLoop);

public sealed record SafeHttpDestination(string Url);

public enum SafeHttpFailureKind
{
    InvalidUrl,
    DestinationRejected,
    NameResolution,
    Connection,
    Tls,
    Timeout,
    Cancelled,
    ResponseHeadersTooLarge,
    RedirectMissingLocation,
    RedirectInvalid,
    RedirectLoop,
    RedirectLimit,
    HttpsDowngrade,
    RequestPolicyRejected,
    Protocol
}

public static class SafeHttpTransportDefaults
{
    public const int DefaultTimeoutSeconds = 15;
    public const int MaxTimeoutSeconds = 300;
    public const int MaxRedirects = 10;
    public const int DefaultMaxResponseBodyBytes = 2 * 1024 * 1024;
    public const int AbsoluteMaxResponseBodyBytes = 8 * 1024 * 1024;

    [Obsolete($"Use {nameof(DefaultMaxResponseBodyBytes)} instead.")]
    public const int MaxDecodedBodyBytes = DefaultMaxResponseBodyBytes;
}
