namespace WebHealth.Web.Models;

/// <summary>
/// A status pill that never relies on colour alone. Each pill carries a text label and a glyph
/// whose shape differs per severity tier, so the state survives greyscale printing, a
/// colour-vision deficiency and a high-contrast theme.
/// </summary>
/// <param name="Status">One of the <c>data-status</c> values the stylesheet defines.</param>
/// <param name="Label">The visible text. It is the primary cue, not a decoration.</param>
/// <param name="Detail">
/// Optional extra wording read only by assistive technology, for a pill whose visible label is
/// short enough to be ambiguous out of context.
/// </param>
/// <param name="Pending">
/// Whether the state the pill names is still being worked on. A pending pill carries a turning
/// mark beside its label, so a page that refreshes itself is distinguishable from one that has
/// stopped updating.
/// </param>
public sealed record StatusBadgeViewModel(
    string Status,
    string Label,
    string? Detail = null,
    bool Pending = false)
{
    /// <summary>
    /// The glyph for a tier. Success, warning and danger get three visually distinct shapes;
    /// anything unrecognised falls back to the neutral information mark rather than to no glyph
    /// at all, so a pill is never colour-only by accident.
    /// </summary>
    public string Icon => Status switch
    {
        "success" => "success",
        "warning" or "high" => "warning",
        "danger" => "error",
        _ => "information"
    };
}
