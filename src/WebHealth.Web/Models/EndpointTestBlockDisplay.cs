using WebHealth.Application.Registry;

namespace WebHealth.Web.Models;

/// <summary>
/// Turns the reason an endpoint cannot be tested into a sentence for the page that would
/// otherwise just be missing its run button.
/// </summary>
public static class EndpointTestBlockDisplay
{
    public static string? Describe(EndpointTestBlock block, string runLabel) => block switch
    {
        EndpointTestBlock.None => null,
        EndpointTestBlock.NotVisible =>
            "This endpoint is no longer available to you, so it cannot be tested.",
        EndpointTestBlock.NotPermitted =>
            $"Your role may read this endpoint but not test it, so {runLabel} cannot be started here.",
        EndpointTestBlock.EndpointDisabled =>
            $"This endpoint is disabled. Enable it on the endpoint page to allow {runLabel}.",
        EndpointTestBlock.EnvironmentInactive =>
            $"The environment this endpoint belongs to is inactive, which stops {runLabel} for every endpoint in it.",
        EndpointTestBlock.WebsiteDisabled =>
            $"The website this endpoint belongs to is disabled, which stops {runLabel} for every endpoint under it.",
        EndpointTestBlock.ClientInactive =>
            $"The client this endpoint belongs to is inactive, which stops {runLabel} for every endpoint under it.",
        EndpointTestBlock.NoMonitor =>
            $"This endpoint has no monitor yet. Add one on the endpoint page to allow {runLabel}.",
        EndpointTestBlock.NoTargetAuthorization =>
            "This endpoint has no current testing authorization for its host and port. Record it on "
            + "the endpoint page: an expired one, or one recorded for a different host or port, does not count.",
        _ => null
    };
}
