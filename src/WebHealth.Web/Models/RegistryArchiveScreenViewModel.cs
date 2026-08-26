namespace WebHealth.Web.Models;

public static class RegistryArchiveScreens
{
    public const string ViewName = "RegistryArchiveScreen";
}

public sealed record RegistryArchiveRow(
    Guid Id,
    long Version,
    string Primary,
    string? Secondary,
    IReadOnlyList<string> Cells);

public sealed record RegistryArchiveScreenViewModel(
    string Title,
    string Subtitle,
    string PrimaryColumn,
    IReadOnlyList<string> Columns,
    IReadOnlyList<RegistryArchiveRow> Rows,
    string DetailsAction,
    string RestoreAction,
    string RestoreConfirmPrompt,
    string CountNoun,
    string EmptyTitle,
    string EmptyDescription,
    string BackAction,
    string ParentLabel,
    string? BackController = null);
