using System.Globalization;

namespace WebHealth.Application.Notifications;

/// <summary>
/// Every field here is bounded, allow-listed data already safe to display (names, normalized
/// identifiers, timestamps) — never raw diagnostics, HTML, or response bodies.
/// </summary>
public sealed record NotificationTemplateData(
    Guid IncidentId,
    string EndpointDisplayUrl,
    string ClientName,
    string WebsiteName,
    string EnvironmentName,
    string IssueKey,
    string Severity,
    DateTimeOffset OpenedAtUtc,
    string OwnerDisplayName,
    string DashboardPath,
    DateTimeOffset? ResolvedAtUtc = null,
    long? OutageDurationMs = null,
    int? UnacknowledgedMinutes = null,
    int? EscalationLevel = null);

public static class NotificationTemplates
{
    public const string Version = "v1";

    public static (string Subject, string Body) RenderOpened(NotificationTemplateData data) => (
        $"[{data.Severity}] {data.EndpointDisplayUrl} is down",
        $"""
        A {data.Severity.ToLowerInvariant()} incident opened for {data.EndpointDisplayUrl}.

        Client / Site / Environment: {data.ClientName} / {data.WebsiteName} / {data.EnvironmentName}
        Issue: {data.IssueKey}
        Opened at (UTC): {Format(data.OpenedAtUtc)}
        Owner: {data.OwnerDisplayName}

        View details: {data.DashboardPath}
        """);

    public static (string Subject, string Body) RenderRecovered(NotificationTemplateData data) => (
        $"[Recovered] {data.EndpointDisplayUrl} is healthy again",
        $"""
        {data.EndpointDisplayUrl} recovered and its incident was resolved.

        Client / Site / Environment: {data.ClientName} / {data.WebsiteName} / {data.EnvironmentName}
        Issue: {data.IssueKey}
        Confirmed recovered at (UTC): {Format(data.ResolvedAtUtc)}
        Outage duration: {FormatDuration(data.OutageDurationMs)}

        View details: {data.DashboardPath}
        """);

    public static (string Subject, string Body) RenderReminder(NotificationTemplateData data) => (
        $"[Reminder] {data.EndpointDisplayUrl} incident still unacknowledged",
        $"""
        The critical incident for {data.EndpointDisplayUrl} has been unacknowledged
        for {data.UnacknowledgedMinutes} minutes.

        Client / Site / Environment: {data.ClientName} / {data.WebsiteName} / {data.EnvironmentName}
        Issue: {data.IssueKey}
        Owner: {data.OwnerDisplayName}

        Acknowledge here: {data.DashboardPath}
        """);

    public static (string Subject, string Body) RenderEscalated(NotificationTemplateData data) => (
        $"[Escalated] {data.EndpointDisplayUrl} incident requires attention",
        $"""
        The critical incident for {data.EndpointDisplayUrl} was escalated to level {data.EscalationLevel}
        after remaining unacknowledged for {data.UnacknowledgedMinutes} minutes.

        Client / Site / Environment: {data.ClientName} / {data.WebsiteName} / {data.EnvironmentName}
        Issue: {data.IssueKey}
        Owner: {data.OwnerDisplayName}

        Acknowledge here: {data.DashboardPath}
        """);

    private static string Format(DateTimeOffset? value) =>
        (value ?? DateTimeOffset.UtcNow).ToString("u", CultureInfo.InvariantCulture);

    private static string FormatDuration(long? milliseconds) => milliseconds is null
        ? "unknown"
        : TimeSpan.FromMilliseconds(milliseconds.Value).ToString("g", CultureInfo.InvariantCulture);
}
