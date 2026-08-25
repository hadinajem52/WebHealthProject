using System.ComponentModel.DataAnnotations;
using WebHealth.Application.Maintenance;
using WebHealth.Domain.Maintenance;
using WebHealth.Web.Shell;

namespace WebHealth.Web.Models;

public sealed record MaintenanceListViewModel(MaintenanceWindowListPage Page)
{
    public IReadOnlyList<MaintenanceWindowListItem> Windows => Page.Items;

    public int ArchivableCount => Page.ArchivableCount;
}

public sealed record MaintenanceArchiveViewModel(MaintenanceWindowListPage Page)
{
    public IReadOnlyList<MaintenanceWindowListItem> Windows => Page.Items;
}

public sealed record MaintenanceDetailsViewModel(MaintenanceWindowDetails Window);

public sealed class MaintenanceWindowFormViewModel
{
    public Guid MaintenanceWindowId { get; set; }
    [Required(ErrorMessage = "Choose whether this window covers an endpoint, an environment, a website or a client.")]
    [Display(Name = "Scope")] public MaintenanceScopeKind ScopeKind { get; set; } = MaintenanceScopeKind.Endpoint;
    [Required(ErrorMessage = "Select the maintenance target.")]
    [Display(Name = "Target")]
    public Guid? ScopeId { get; set; }
    [Required(ErrorMessage = "Enter when the maintenance starts.")]
    [Display(Name = "Starts at (UTC)")] public DateTime StartsAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(5);
    [Required(ErrorMessage = "Enter when the maintenance ends.")]
    [Display(Name = "Ends at (UTC)")] public DateTime EndsAtUtc { get; set; } = DateTime.UtcNow.AddHours(1);
    [Required(ErrorMessage = "Select a time zone.")]
    [StringLength(100, ErrorMessage = "That time zone identifier is too long.")]
    [Display(Name = "Display timezone")] public string TimezoneId { get; set; } = "UTC";
    [Required(ErrorMessage = "Enter why this maintenance window exists. It is kept with the record.")]
    [StringLength(500, ErrorMessage = "This reason is too long. Use 500 characters or fewer.")]
    public string Reason { get; set; } = string.Empty;
    [Required(ErrorMessage = "Choose whether notifications are suppressed while this window is active.")]
    [Display(Name = "Notification policy")] public string SuppressionPolicy { get; set; } = "SuppressAll";
    [Display(Name = "Pause escalation while active")] public bool PauseEscalation { get; set; } = true;
    [Display(Name = "Continue failure confirmation after maintenance")] public bool ContinueFailureCounter { get; set; }
    [Required(ErrorMessage = "Choose how often this window repeats.")]
    [Display(Name = "Repeats")] public string RecurrencePattern { get; set; } = MaintenanceRecurrencePatterns.None;
    [Display(Name = "Repeat on")] public bool[] RecurrenceDays { get; set; } = new bool[7];
    [Display(Name = "Repeat until (UTC, optional)")] public DateTime? RecurrenceUntilUtc { get; set; }
    public long Version { get; set; }
    public IReadOnlyList<MaintenanceScopeOption> ScopeOptions { get; set; } = [];

    public int RecurrenceDaysMask => Enum.GetValues<DayOfWeek>()
        .Where(day => RecurrenceDays.Length > (int)day && RecurrenceDays[(int)day])
        .Aggregate(MaintenanceDayOfWeekMask.Empty, (mask, day) => mask | MaintenanceDayOfWeekMask.Of(day));
}

/// <summary>
/// One status vocabulary for a maintenance window, whichever shape the page is holding. The list
/// and the detail page each had their own, and they disagreed: a window the list called Finished
/// read as Scheduled on its own page.
/// </summary>
public static class MaintenanceStatusDisplay
{
    public static string Name(MaintenanceWindowListItem window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return Name(window.IsCancelled, window.IsFinished);
    }

    public static string Name(MaintenanceWindowDetails window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return Name(window.IsCancelled, window.IsFinished);
    }

    public static string Badge(MaintenanceWindowListItem window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return Badge(window.IsCancelled, window.IsFinished);
    }

    public static string Badge(MaintenanceWindowDetails window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return Badge(window.IsCancelled, window.IsFinished);
    }

    /// <summary>
    /// What the status means, for the badge's own tooltip. A window is a schedule, not a job, so
    /// "Finished" and "Cancelled" say different things about the suppression that was in force.
    /// </summary>
    public static string Detail(bool isCancelled, bool isFinished, bool isArchived)
    {
        var state = isCancelled
            ? "Cancelled before it could run again. Checks already taken keep their evidence."
            : isFinished
                ? "Every occurrence has passed and none remain, so nothing is suppressed any more."
                : "Occurrences remain. Checks keep running while one is active; the notification policy below decides what is sent.";
        return isArchived ? $"{state} It is filed in the archive and can be restored unchanged." : state;
    }

    public static string Detail(MaintenanceWindowListItem window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return Detail(window.IsCancelled, window.IsFinished, window.ArchivedAt is not null);
    }

    public static string Detail(MaintenanceWindowDetails window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return Detail(window.IsCancelled, window.IsFinished, window.ArchivedAt is not null);
    }

    private static string Name(bool isCancelled, bool isFinished) =>
        isCancelled ? "Cancelled" : isFinished ? "Finished" : "Scheduled";

    private static string Badge(bool isCancelled, bool isFinished) =>
        isCancelled ? StatusBadges.Danger : isFinished ? StatusBadges.Neutral : StatusBadges.Warning;
}

public static class MaintenanceRecurrenceDisplay
{
    public static string Describe(MaintenanceRecurrenceSpec recurrence) => recurrence.Pattern switch
    {
        MaintenanceRecurrencePatterns.Daily => Bounded("Daily", recurrence.Until),
        MaintenanceRecurrencePatterns.Weekly => Bounded($"Weekly · {Days(recurrence.DaysOfWeekMask)}", recurrence.Until),
        _ => "One-off"
    };

    private static string Days(int mask) => string.Join(", ", Enum.GetValues<DayOfWeek>()
        .Where(day => MaintenanceDayOfWeekMask.Includes(mask, day))
        .Select(day => day.ToString()[..3]));

    private static string Bounded(string label, DateTimeOffset? until) =>
        until is null ? label : $"{label} until {until.Value:yyyy-MM-dd}";
}
