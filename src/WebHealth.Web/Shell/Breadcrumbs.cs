using Microsoft.AspNetCore.Mvc;

namespace WebHealth.Web.Shell;

public static class Breadcrumbs
{
    public static BreadcrumbItem Registry(IUrlHelper url) =>
        new("Registry", url.Action("Clients", "Registry"));

    public static BreadcrumbItem Clients(IUrlHelper url) =>
        new("Clients", url.Action("Clients", "Registry"));

    public static BreadcrumbItem Websites(IUrlHelper url) =>
        new("Websites", url.Action("Websites", "Registry"));

    public static BreadcrumbItem Endpoints(IUrlHelper url) =>
        new("Endpoints", url.Action("Endpoints", "Targets"));

    public static BreadcrumbItem Website(IUrlHelper url, Guid websiteId, string name) =>
        new(name, url.Action("Website", "Registry", new { id = websiteId }));

    public static BreadcrumbItem Environments(IUrlHelper url, Guid websiteId) =>
        new("Environments", url.Action("Environments", "Targets", new { websiteId }));

    public static BreadcrumbItem Environment(IUrlHelper url, Guid environmentId, string name) =>
        new(name, url.Action("Environment", "Targets", new { id = environmentId }));

    public static BreadcrumbItem Endpoint(IUrlHelper url, Guid endpointId, string displayUrl) =>
        new(displayUrl, url.Action("Endpoint", "Targets", new { id = endpointId }));

    public static BreadcrumbItem Administration(IUrlHelper url) =>
        new("Administration", url.Action("Users", "Administration"));
}
