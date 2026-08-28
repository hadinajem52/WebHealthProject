namespace WebHealth.Web.Models;

public sealed record RunHistoryArchiveRow(
    Guid Id,
    string Primary,
    string? Secondary,
    StatusBadgeViewModel Status,
    IReadOnlyList<string> Cells,
    string DetailsUrl);

public sealed record RunHistoryArchiveScreenViewModel(
    string Title,
    string Subtitle,
    string PrimaryColumn,
    IReadOnlyList<string> Columns,
    IReadOnlyList<RunHistoryArchiveRow> Rows,
    string CountNoun,
    string ParentLabel,
    string BackUrl,
    string RestoreUrl,
    bool CanRestore,
    string EmptyTitle,
    string EmptyDescription,
    string? AjaxTarget = null);

public sealed record ClearHistoryActionViewModel(
    string ArchiveUrl,
    string ArchivedUrl,
    bool CanArchive,
    bool HasClearableRows,
    string ButtonTitle,
    string ConfirmPrompt,
    string? AjaxTarget = null);
