using WebHealth.Domain.Incidents;

namespace WebHealth.Application.Incidents;

public enum IncidentLifecycleAction
{
    Acknowledge,
    StartProgress,
    BeginRecovery,
    InterruptRecovery,
    ConfirmRecovery,
    ResolveManually,
    Close,
    ForceClose,
    Reopen
}

public sealed record EvaluateIncidentTransition(
    string CurrentStatus,
    IncidentLifecycleAction Action,
    bool IsAdministrator = false,
    bool WasAcknowledged = false,
    string? Category = null,
    string? NoteOrReason = null);

public sealed record IncidentTransitionDecision(
    bool Succeeded,
    string? NewStatus,
    string? ResolutionCategory,
    string? ResolutionNote,
    IReadOnlyList<string> Errors)
{
    public static IncidentTransitionDecision Success(
        string status,
        string? category = null,
        string? note = null) => new(true, status, category, note, []);

    public static IncidentTransitionDecision Failure(string error) =>
        new(false, null, null, null, [error]);
}

public static class IncidentLifecycleEngine
{
    public const int MaximumCategoryLength = 50;
    public const int MaximumNoteLength = 2000;
    public const int MaximumAdministratorReasonLength = 500;
    public static readonly TimeSpan RecurrenceWindow = TimeSpan.FromDays(30);

    public static IncidentTransitionDecision Evaluate(EvaluateIncidentTransition input) => input.Action switch
    {
        IncidentLifecycleAction.Acknowledge => From(
            input.CurrentStatus == IncidentStatuses.Open,
            IncidentStatuses.Acknowledged,
            "Only an open incident can be acknowledged."),
        IncidentLifecycleAction.StartProgress => From(
            input.CurrentStatus == IncidentStatuses.Acknowledged,
            IncidentStatuses.InProgress,
            "Only an acknowledged incident can move in progress."),
        IncidentLifecycleAction.BeginRecovery => From(
            IncidentStatuses.Active.Contains(input.CurrentStatus)
                && input.CurrentStatus != IncidentStatuses.MonitoringRecovery,
            IncidentStatuses.MonitoringRecovery,
            "Only an active incident can begin recovery monitoring."),
        IncidentLifecycleAction.InterruptRecovery => From(
            input.CurrentStatus == IncidentStatuses.MonitoringRecovery,
            input.WasAcknowledged ? IncidentStatuses.InProgress : IncidentStatuses.Open,
            "Only an incident monitoring recovery can return to failure."),
        IncidentLifecycleAction.ConfirmRecovery => ResolveAutomatically(input.CurrentStatus),
        IncidentLifecycleAction.ResolveManually => ResolveManually(input),
        IncidentLifecycleAction.Close => From(
            input.CurrentStatus == IncidentStatuses.Resolved,
            IncidentStatuses.Closed,
            "Only a resolved incident can be closed normally."),
        IncidentLifecycleAction.ForceClose => ForceClose(input),
        IncidentLifecycleAction.Reopen => Reopen(input),
        _ => throw new ArgumentOutOfRangeException(nameof(input))
    };

    public static bool IsWithinRecurrenceWindow(DateTimeOffset previousClosedAt, DateTimeOffset openedAt) =>
        openedAt >= previousClosedAt && openedAt <= previousClosedAt.Add(RecurrenceWindow);

    public static long DurationMilliseconds(DateTimeOffset startsAt, DateTimeOffset endsAt) =>
        (long)Math.Clamp(Math.Ceiling((endsAt - startsAt).TotalMilliseconds), 0, long.MaxValue);

    private static IncidentTransitionDecision ResolveAutomatically(string status) =>
        IncidentStatuses.Active.Contains(status)
            ? IncidentTransitionDecision.Success(
                IncidentStatuses.Resolved,
                IncidentResolutionCategories.AutomaticRecovery,
                "Recovery was confirmed by scheduled monitoring evidence.")
            : IncidentTransitionDecision.Failure(
                "Only an active incident can be automatically resolved.");

    private static IncidentTransitionDecision ResolveManually(EvaluateIncidentTransition input)
    {
        if (!IncidentStatuses.Active.Contains(input.CurrentStatus))
        {
            return IncidentTransitionDecision.Failure("Only an active incident can be manually resolved.");
        }

        var category = input.Category?.Trim();
        var note = input.NoteOrReason?.Trim();
        if (category is null or { Length: 0 } || category.Length > MaximumCategoryLength)
        {
            return IncidentTransitionDecision.Failure(
                $"A resolution category of up to {MaximumCategoryLength} characters is required.");
        }

        return note is null or { Length: 0 } || note.Length > MaximumNoteLength
            ? IncidentTransitionDecision.Failure(
                $"A resolution note of up to {MaximumNoteLength} characters is required.")
            : IncidentTransitionDecision.Success(IncidentStatuses.Resolved, category, note);
    }

    private static IncidentTransitionDecision ForceClose(EvaluateIncidentTransition input)
    {
        if (!input.IsAdministrator)
        {
            return IncidentTransitionDecision.Failure("Only an administrator can force close an incident.");
        }

        if (input.CurrentStatus == IncidentStatuses.Closed)
        {
            return IncidentTransitionDecision.Failure("The incident is already closed.");
        }

        var reason = ValidateAdministratorReason(input.NoteOrReason);
        return reason is null
            ? IncidentTransitionDecision.Failure(
                $"An administrator reason of up to {MaximumAdministratorReasonLength} characters is required.")
            : IncidentTransitionDecision.Success(
                IncidentStatuses.Closed,
                IncidentResolutionCategories.ForcedClosure,
                reason);
    }

    private static IncidentTransitionDecision Reopen(EvaluateIncidentTransition input)
    {
        if (!input.IsAdministrator || input.CurrentStatus != IncidentStatuses.Closed)
        {
            return IncidentTransitionDecision.Failure(
                "Only an administrator can reopen a closed incident.");
        }

        var reason = ValidateAdministratorReason(input.NoteOrReason);
        return reason is null
            ? IncidentTransitionDecision.Failure(
                $"An administrator reason of up to {MaximumAdministratorReasonLength} characters is required.")
            : IncidentTransitionDecision.Success(IncidentStatuses.Open, null, reason);
    }

    private static string? ValidateAdministratorReason(string? value)
    {
        var reason = value?.Trim();
        return reason is null or { Length: 0 } || reason.Length > MaximumAdministratorReasonLength
            ? null
            : reason;
    }

    private static IncidentTransitionDecision From(bool allowed, string status, string error) =>
        allowed
            ? IncidentTransitionDecision.Success(status)
            : IncidentTransitionDecision.Failure(error);
}
