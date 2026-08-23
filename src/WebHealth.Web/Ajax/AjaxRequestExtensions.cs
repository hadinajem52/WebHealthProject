namespace WebHealth.Web.Ajax;

public static class AjaxRequestExtensions
{
    public static bool IsWebHealthAjax(this HttpRequest request) =>
        request.Headers.TryGetValue(AjaxResponseHeaders.Request, out var values)
        && values.Any(value => string.Equals(value, "1", StringComparison.Ordinal));
}
