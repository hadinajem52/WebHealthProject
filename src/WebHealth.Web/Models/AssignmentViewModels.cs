using System.ComponentModel.DataAnnotations;
using WebHealth.Application.Administration;
using WebHealth.Application.Assignments;

namespace WebHealth.Web.Models;

public sealed class TeamListViewModel
{
    public required IReadOnlyList<ManagedTeam> Teams { get; init; }
}

public sealed class TeamFormViewModel
{
    public Guid TeamId { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Team disabled")]
    public bool IsDisabled { get; set; }

    public long Version { get; set; }

    [Display(Name = "Members")]
    public List<Guid> MemberUserIds { get; set; } = [];

    public IReadOnlyList<ManagedUser> AvailableUsers { get; set; } = [];
}
