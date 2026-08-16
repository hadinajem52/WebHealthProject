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

public interface IMonitoringTargetAuthorizer
{
    Task<bool> IsAuthorizedAsync(
        Guid endpointId,
        string normalizedHost,
        int port,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);
}

public sealed record SafeHttpTransportRequest(
    Guid EndpointId,
    string Url,
    bool IsProduction,
    int MaxRedirects = SafeHttpTransportDefaults.MaxRedirects,
    int MaxResponseBodyBytes = SafeHttpTransportDefaults.MaxDecodedBodyBytes);

public sealed record SafeHttpTransportResult(
    SafeHttpFailureKind? Failure,
    int? StatusCode,
    SafeHttpDestination? FinalDestination,
    TimeSpan Duration,
    long ResponseBytesRead,
    bool BodyTruncated,
    ReadOnlyMemory<byte> Body,
    IReadOnlyList<SafeHttpRedirectHop> Redirects,
    string? RequestIdentity = null)
{
    public bool Succeeded => Failure is null;
}

public sealed record SafeHttpRedirectHop(
    int StatusCode,
    string FromUrl,
    string ToUrl,
    bool IsLoop);

public sealed record SafeHttpDestination(string Url);

public enum SafeHttpFailureKind
{
    InvalidUrl,
    TargetNotAuthorized,
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
    Protocol
}

public static class SafeHttpTransportDefaults
{
    public const int MaxRedirects = 10;
    public const int MaxDecodedBodyBytes = 2 * 1024 * 1024;
}
