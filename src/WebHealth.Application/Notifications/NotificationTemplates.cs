using System.Globalization;

namespace WebHealth.Application.Notifications;

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

    public static (string Subject, string Body) RenderOpened(NotificationTemplateData data) =>
        IsPageSpeed(data.IssueKey) ? RenderPageSpeedOpened(data) : (
        $"[{data.Severity}] {data.EndpointDisplayUrl} is down",
        $"""
        A {data.Severity.ToLowerInvariant()} incident opened for {data.EndpointDisplayUrl}.

        Client / Site / Environment: {data.ClientName} / {data.WebsiteName} / {data.EnvironmentName}
        Issue: {data.IssueKey}
        Opened at (UTC): {Format(data.OpenedAtUtc)}
        Owner: {data.OwnerDisplayName}

        View details: {data.DashboardPath}
        """);

    public static (string Subject, string Body) RenderRecovered(NotificationTemplateData data) =>
        IsPageSpeed(data.IssueKey) ? RenderPageSpeedRecovered(data) : (
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

    private static (string Subject, string Body) RenderPageSpeedOpened(NotificationTemplateData data) => (
        $"[{data.Severity}] PageSpeed threshold breached for {data.EndpointDisplayUrl}",
        $"""
        A {data.Severity.ToLowerInvariant()} PageSpeed incident opened for {data.EndpointDisplayUrl}.

        Client / Site / Environment: {data.ClientName} / {data.WebsiteName} / {data.EnvironmentName}
        Issue: {data.IssueKey}
        Measured at (UTC): {Format(data.OpenedAtUtc)}
        Owner: {data.OwnerDisplayName}

        View details: {data.DashboardPath}
        """);

    private static (string Subject, string Body) RenderPageSpeedRecovered(NotificationTemplateData data) => (
        $"[Recovered] PageSpeed threshold cleared for {data.EndpointDisplayUrl}",
        $"""
        {data.EndpointDisplayUrl} returned within its configured PageSpeed threshold and the incident was resolved.

        Client / Site / Environment: {data.ClientName} / {data.WebsiteName} / {data.EnvironmentName}
        Issue: {data.IssueKey}
        Confirmed recovered at (UTC): {Format(data.ResolvedAtUtc)}
        Incident duration: {FormatDuration(data.OutageDurationMs)}

        View details: {data.DashboardPath}
        """);

    private static bool IsPageSpeed(string issueKey) =>
        issueKey.Contains("|PageSpeedInsights|", StringComparison.Ordinal);

    private static string Format(DateTimeOffset? value) =>
        (value ?? DateTimeOffset.UtcNow).ToString("u", CultureInfo.InvariantCulture);

    private static string FormatDuration(long? milliseconds) => milliseconds is null
        ? "unknown"
        : TimeSpan.FromMilliseconds(milliseconds.Value).ToString("g", CultureInfo.InvariantCulture);
}
