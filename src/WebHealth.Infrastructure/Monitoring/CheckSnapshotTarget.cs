using WebHealth.Domain.Normalization;
using WebHealth.Application.Monitoring;

namespace WebHealth.Infrastructure.Monitoring;

internal static class CheckSnapshotTarget
{
    public static ResolvedMonitoringTarget Resolve(LogicalCheck check)
    {
        var snapshot = check.ConfigurationSnapshot;
        if (snapshot.SchemaVersion == 1)
        {
            var endpoint = check.EndpointMonitor.Endpoint;
            return new(endpoint.Id, endpoint.NormalizedUrl, endpoint.NormalizedHost,
                endpoint.EffectivePort, endpoint.NormalizationVersion, endpoint.Environment.IsProduction);
        }
        if (snapshot is
            {
                SchemaVersion: 2,
                TargetNormalizedUrl: { } url,
                TargetNormalizedHost: { } host,
                TargetEffectivePort: >= 1 and <= 65535,
                TargetNormalizationVersion: > 0,
                TargetIsProduction: not null,
                CurrentTruthGeneration: > 0
            })
        {
            var normalized = EndpointUrlNormalizer.Normalize(url);
            if (normalized.Succeeded && normalized.NormalizedUrl == url
                && normalized.NormalizedHost == host && normalized.EffectivePort == snapshot.TargetEffectivePort)
            {
                return new(check.EndpointMonitor.EndpointId, url, host, snapshot.TargetEffectivePort.Value,
                    snapshot.TargetNormalizationVersion.Value, snapshot.TargetIsProduction.Value);
            }
        }
        throw new InvalidOperationException("The check has an unsupported or incomplete target snapshot.");
    }
}
