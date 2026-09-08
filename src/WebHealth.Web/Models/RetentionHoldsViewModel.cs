using System.ComponentModel.DataAnnotations;
using WebHealth.Application.Monitoring;

namespace WebHealth.Web.Models;

public sealed class RetentionHoldsViewModel
{
    [Required]
    public string ScopeType { get; set; } = "Endpoint";
    public Guid ScopeId { get; set; }
    [Required, StringLength(500)]
    public string Reason { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public int Offset { get; set; }
    public DateTimeOffset AsOf { get; set; }
    public IReadOnlyList<RetentionHoldView> Holds { get; set; } = [];
}
