namespace WebHealth.Web.Models;

public sealed record RegistryActionRow(
    Guid Id,
    long Version,
    string Primary,
    string? Secondary,
    IReadOnlyList<string> Cells,
    string StatusLabel,
    string StatusTone);

public sealed record RegistryActionScreenViewModel(
    string Title,
    string Subtitle,
    string PrimaryColumn,
    IReadOnlyList<string> Columns,
    IReadOnlyList<RegistryActionRow> Rows,
    string FormAction,
    string DetailsAction,
    string ButtonLabel,
    string ButtonIcon,
    bool IsDestructive,
    string ConfirmPrompt,
    string EmptyTitle,
    string EmptyDescription,
    string BackAction,
    string ParentLabel);
