using WebHealth.Application.Monitoring;
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

    public static string ForSeverity(string? severity) => severity switch
    {
        FindingSeverities.Critical => Danger,
        FindingSeverities.High => High,
        _ => Warning
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
