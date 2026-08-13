namespace WebHealth.Web.Shell;

/// <summary>Severity of a flash message. Rendering pairs every level with a text label and an icon.</summary>
public enum FlashLevel
{
    /// <summary>A completed operation.</summary>
    Success = 0,

    /// <summary>Neutral context that requires no action.</summary>
    Information = 1,

    /// <summary>A condition the operator should review.</summary>
    Warning = 2,

    /// <summary>A failed operation.</summary>
    Error = 3
}

/// <summary>
/// A one-time message shown after a redirect. Text is treated as untrusted and
/// is always output-encoded by the view.
/// </summary>
/// <param name="Level">Severity of the message.</param>
/// <param name="Text">Safe, user-facing text. Never include secrets or exception detail.</param>
public sealed record FlashMessage(FlashLevel Level, string Text);
