using WebHealth.Application.Monitoring;
using WebHealth.Domain.Health;
using WebHealth.Domain.Incidents;
using WebHealth.Domain.Monitoring;

namespace WebHealth.Web.Shell;

public static class StatusBadges
{
    public const string Success = "success";
    public const string Warning = "warning";
    public const string High = "high";
    public const string Danger = "danger";
    public const string Acknowledged = "acknowledged";
    public const string Info = "info";

    public const string Neutral = "neutral";

    public static string Icon(string status) => status switch
    {
        Success => "success",
        Warning or High => "warning",
        Danger => "error",
        _ => "information"
    };

    public static string ForSeverity(string? severity) => severity switch
    {
        FindingSeverities.Critical => Danger,
        FindingSeverities.High => High,
        _ => Warning
    };

    public static string ForHealthStatus(string? status) => status switch
    {
        EndpointHealthStatuses.Healthy => Success,
        EndpointHealthStatuses.Critical => Danger,
        EndpointHealthStatuses.Warning => Warning,
        _ => Neutral
    };

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

    public static string DescribeExpirySeverity(CertificateExpirySeverity severity) => severity switch
    {
        CertificateExpirySeverity.Critical => "Expiring — critical",
        CertificateExpirySeverity.High => "Expiring — high",
        CertificateExpirySeverity.Warning => "Expiring — warning",
        _ => "Not expiring soon"
    };
}
