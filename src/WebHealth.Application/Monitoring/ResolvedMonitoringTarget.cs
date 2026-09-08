namespace WebHealth.Application.Monitoring;

public sealed record ResolvedMonitoringTarget(
    Guid EndpointId,
    string NormalizedUrl,
    string NormalizedHost,
    int EffectivePort,
    int NormalizationVersion,
    bool IsProduction);
