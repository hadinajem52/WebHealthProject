using WebHealth.Application.Monitoring;
using WebHealth.Domain.Health;
using WebHealth.Domain.Incidents;
using WebHealth.Domain.Monitoring;

namespace WebHealth.Web.Shell;

/// <summary>
/// Maps the vocabularies the views display onto the badge styles the stylesheet defines.
/// It exists so the three severity bands (BR-C04) read the same way on every page — a rule the
/// views were previously each free to get slightly wrong on their own.
/// </summary>
public static class StatusBadges
{
    public const string Success = "success";
    public const string Warning = "warning";
    public const string High = "high";
    public const string Danger = "danger";
    public const string Acknowledged = "acknowledged";
    public const string Info = "info";

    /// <summary>
    /// Neither a verdict nor information: the state of something that is switched off or has
    /// not reported. Figma node 1633:352 draws Disabled and Unknown in one grey for that reason.
    /// </summary>
    public const string Neutral = "neutral";

    public static string ForSeverity(string? severity) => severity switch
    {
        FindingSeverities.Critical => Danger,
        FindingSeverities.High => High,
        _ => Warning
    };

    /// <summary>
    /// The badge style for a confirmed endpoint health state. <c>Unknown</c> and
    /// <c>Disabled</c> are neutral rather than a warning: an endpoint that has not reported yet,
    /// or that nothing is checking, is not a problem.
    /// </summary>
    public static string ForHealthStatus(string? status) => status switch
    {
        EndpointHealthStatuses.Healthy => Success,
        EndpointHealthStatuses.Critical => Danger,
        EndpointHealthStatuses.Warning => Warning,
        _ => Neutral
    };

    /// <summary>
    /// The badge style for an incident's lifecycle status. Acknowledged carries its own fill
    /// (Figma node 1633:352) rather than sharing Open's: someone has picked the incident up,
    /// which is not the same as nobody having looked at it yet.
    /// </summary>
    /// <remarks>
    /// It lives here rather than in the incident views because the list and the detail page were
    /// each carrying their own copy of the same switch, and a status added to one of them would
    /// have been styled differently on the other.
    /// </remarks>
    public static string ForIncidentStatus(string? status) => status switch
    {
        IncidentStatuses.Open => Danger,
        IncidentStatuses.Acknowledged => Acknowledged,
        IncidentStatuses.InProgress or IncidentStatuses.MonitoringRecovery => Warning,
        _ => Success
    };

    public static string ForOutcome(string? outcome) => outcome switch
    {
        HttpResultOutcomes.Healthy => Success,
        HttpResultOutcomes.Critical => Danger,
        _ => Warning
    };

    public static string ForExpirySeverity(CertificateExpirySeverity severity) => severity switch
    {
        CertificateExpirySeverity.Critical => Danger,
        CertificateExpirySeverity.High => High,
        CertificateExpirySeverity.Warning => Warning,
        _ => Success
    };

    /// <summary>
    /// The label for an expiry band, phrased as what it means rather than as its enum name:
    /// a certificate outside every band is simply valid, not "None".
    /// </summary>
    public static string DescribeExpirySeverity(CertificateExpirySeverity severity) => severity switch
    {
        CertificateExpirySeverity.Critical => "Expiring — critical",
        CertificateExpirySeverity.High => "Expiring — high",
        CertificateExpirySeverity.Warning => "Expiring — warning",
        _ => "Not expiring soon"
    };
}
