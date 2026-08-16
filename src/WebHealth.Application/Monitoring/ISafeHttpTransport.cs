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

public sealed record SafeHttpTransportRequest(Guid EndpointId, string Url, bool IsProduction);

public sealed record SafeHttpTransportResult(
    SafeHttpFailureKind? Failure,
    int? StatusCode,
    SafeHttpDestination? FinalDestination,
    TimeSpan Duration,
    long ResponseBytesRead,
    bool BodyTruncated,
    ReadOnlyMemory<byte> Body,
    IReadOnlyList<SafeHttpRedirectHop> Redirects)
{
    public bool Succeeded => Failure is null;
}

public sealed record SafeHttpRedirectHop(
    int StatusCode,
    string FromScheme,
    string FromHost,
    int FromPort,
    string ToScheme,
    string ToHost,
    int ToPort);

public sealed record SafeHttpDestination(string Scheme, string Host, int Port);

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
