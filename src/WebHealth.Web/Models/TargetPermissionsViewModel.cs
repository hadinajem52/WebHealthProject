using System.ComponentModel.DataAnnotations;
using WebHealth.Application.Monitoring;

namespace WebHealth.Web.Models;

public sealed class TargetPermissionsViewModel
{
    public Guid EndpointId { get; set; }
    [Required, StringLength(2048)]
    public string Url { get; set; } = "";
    [Required]
    public string Kind { get; set; } = "Owned";
    [Required, StringLength(500)]
    public string EvidenceReference { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }
    public TargetPermissions? Permissions { get; set; }
}
