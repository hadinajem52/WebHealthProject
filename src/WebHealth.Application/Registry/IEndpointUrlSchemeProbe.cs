using WebHealth.Domain.Normalization;

namespace WebHealth.Application.Registry;

public enum EndpointSchemeProbeOutcome
{
    Responded,
    SchemeUnavailable,
    HostUnavailable
}

public interface IEndpointUrlSchemeProbe
{
    Task<EndpointSchemeProbeOutcome> ProbeAsync(string url, CancellationToken cancellationToken = default);
}

public enum EndpointUrlSchemeSource
{
    AsTyped,
    Detected,
    Assumed
}

public sealed record EndpointUrlResolution(string? Value, EndpointUrlSchemeSource Source)
{
    public bool SchemeAssumed => Source == EndpointUrlSchemeSource.Assumed;

    public static EndpointUrlResolution Unchanged(string? value) =>
        new(value, EndpointUrlSchemeSource.AsTyped);
}

public sealed class EndpointUrlResolver(IEndpointUrlSchemeProbe probe)
{
    public static EndpointUrlResolution Coerce(string? value)
    {
        var interpretation = EndpointUrlInput.Interpret(value);
        return interpretation.NeedsSchemeProbe
            ? new(interpretation.HttpsCandidate, EndpointUrlSchemeSource.Assumed)
            : EndpointUrlResolution.Unchanged(value);
    }

    public async Task<EndpointUrlResolution> ResolveAsync(
        string? value,
        CancellationToken cancellationToken = default)
    {
        var interpretation = EndpointUrlInput.Interpret(value);
        if (!interpretation.NeedsSchemeProbe)
        {
            return EndpointUrlResolution.Unchanged(value);
        }

        var https = interpretation.HttpsCandidate!;
        var outcome = await probe.ProbeAsync(https, cancellationToken);
        if (outcome == EndpointSchemeProbeOutcome.Responded)
        {
            return new(https, EndpointUrlSchemeSource.Detected);
        }

        if (outcome == EndpointSchemeProbeOutcome.HostUnavailable
            || interpretation.HttpCandidate is not { } http)
        {
            return new(https, EndpointUrlSchemeSource.Assumed);
        }

        return await probe.ProbeAsync(http, cancellationToken) == EndpointSchemeProbeOutcome.Responded
            ? new(http, EndpointUrlSchemeSource.Detected)
            : new(https, EndpointUrlSchemeSource.Assumed);
    }
}
