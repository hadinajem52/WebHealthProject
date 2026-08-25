namespace WebHealth.Application;

public sealed record ValidationError(string? Field, string Message)
{
    public static implicit operator ValidationError(string message) => new(null, message);

    public static ValidationError For(string field, string message) => new(field, message);

    public override string ToString() => Message;
}
