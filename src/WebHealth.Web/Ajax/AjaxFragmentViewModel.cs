namespace WebHealth.Web.Ajax;

public sealed record AjaxFragmentViewModel(
    string? Message = null,
    string Level = "success",
    string? RedirectUrl = null,
    string? RefreshUrl = null,
    string? StatusUrl = null,
    Guid? RunId = null);
