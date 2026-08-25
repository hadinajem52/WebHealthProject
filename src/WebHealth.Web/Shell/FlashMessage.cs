namespace WebHealth.Web.Shell;

public enum FlashLevel
{
    Success = 0,

    Information = 1,

    Warning = 2,

    Error = 3
}

public sealed record FlashMessage(FlashLevel Level, string Text);
