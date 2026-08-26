using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebHealth.Application.Registry;
using WebHealth.Domain.PageAudits;
using WebHealth.Infrastructure.Identity;
using WebHealth.IntegrationTests.Support;
using WebHealth.Web.Ajax;
using WebHealth.Web.Models;
using Xunit;

namespace WebHealth.IntegrationTests;

public sealed class EndpointRegistrationFormTests(WebHealthWebApplicationFactory factory)
    : IClassFixture<WebHealthWebApplicationFactory>
{
    [Fact]
    public async Task RegistrationPageOffersEveryPlacementLevelAndKeepsAdvancedSettingsCollapsed()
    {
        var registration = new RecordingEndpointRegistrationService();
        using var configuredFactory = CreateFactory(registration);
        using var client = CreateClient(configuredFactory, true, ApplicationRoles.Administrator);

        var content = await client.GetStringAsync("/Targets/RegisterEndpoint");

        Assert.Contains("Endpoint registration", content, StringComparison.Ordinal);
        Assert.Contains("data-registration-picker=\"client\"", content, StringComparison.Ordinal);
        Assert.Contains("data-registration-picker=\"website\"", content, StringComparison.Ordinal);
        Assert.Contains("data-registration-picker=\"environment\"", content, StringComparison.Ordinal);
        Assert.Contains("Create a new client", content, StringComparison.Ordinal);
        Assert.Contains("Create a new website", content, StringComparison.Ordinal);
        Assert.Contains("Create a new environment", content, StringComparison.Ordinal);
        Assert.Contains("Advanced settings", content, StringComparison.Ordinal);
        Assert.Contains("Client notes", content, StringComparison.Ordinal);
        Assert.Contains("Environment base URL", content, StringComparison.Ordinal);
        Assert.Contains("Monitoring interval override", content, StringComparison.Ordinal);
        Assert.DoesNotMatch("<details[^>]*data-registration-advanced[^>]* open", content);
        Assert.Empty(registration.Requests);
    }

    [Fact]
    public async Task RegistrationPageIsForbiddenToANonManagingRole()
    {
        using var client = factory.CreateHttpsClientWithoutRedirects(ApplicationRoles.Viewer);

        using var response = await client.GetAsync("/Targets/RegisterEndpoint");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("clientId")]
    [InlineData("websiteId")]
    [InlineData("environmentId")]
    public async Task PrefillSelectsTheSuppliedLevelAndEveryLevelAboveIt(string parameter)
    {
        var registration = new RecordingEndpointRegistrationService();
        using var configuredFactory = CreateFactory(registration);
        using var client = CreateClient(configuredFactory, true, ApplicationRoles.Administrator);
        var id = parameter switch
        {
            "clientId" => RegistrationFormReader.ClientId,
            "websiteId" => RegistrationFormReader.WebsiteId,
            _ => RegistrationFormReader.EnvironmentId
        };

        var content = await client.GetStringAsync($"/Targets/RegisterEndpoint?{parameter}={id}");

        AssertSelected(content, "ClientId", RegistrationFormReader.ClientId.ToString());
        if (parameter != "clientId")
        {
            AssertSelected(content, "WebsiteId", RegistrationFormReader.WebsiteId.ToString());
        }

        if (parameter == "environmentId")
        {
            AssertSelected(content, "EnvironmentId", RegistrationFormReader.EnvironmentId.ToString());
        }
    }

    [Fact]
    public async Task EnvironmentPrefillWinsAndWebsiteWithoutEnvironmentsRemainsAvailable()
    {
        var registration = new RecordingEndpointRegistrationService();
        using var configuredFactory = CreateFactory(registration);
        using var client = CreateClient(configuredFactory, true, ApplicationRoles.Administrator);
        var content = await client.GetStringAsync(
            $"/Targets/RegisterEndpoint?clientId={Guid.NewGuid()}&websiteId={Guid.NewGuid()}&environmentId={RegistrationFormReader.EnvironmentId}");

        AssertSelected(content, "ClientId", RegistrationFormReader.ClientId.ToString());
        AssertSelected(content, "WebsiteId", RegistrationFormReader.WebsiteId.ToString());
        AssertSelected(content, "EnvironmentId", RegistrationFormReader.EnvironmentId.ToString());

        content = await client.GetStringAsync(
            $"/Targets/RegisterEndpoint?websiteId={RegistrationFormReader.EmptyWebsiteId}");

        AssertSelected(content, "ClientId", RegistrationFormReader.ClientId.ToString());
        AssertSelected(content, "WebsiteId", RegistrationFormReader.EmptyWebsiteId.ToString());
        Assert.Contains("Website with no environments", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EndpointRegistrationModes.ExistingEnvironment, "EnvironmentBaseUrl", "https://preserved.example.test/")]
    [InlineData(EndpointRegistrationModes.NewEnvironment, "EnvironmentName", "Preserved environment")]
    [InlineData(EndpointRegistrationModes.NewWebsite, "WebsiteName", "Preserved website")]
    [InlineData(EndpointRegistrationModes.NewClient, "ClientName", "Preserved client")]
    public async Task AjaxValidationPreservesEveryPlacementAndItsValues(
        string mode,
        string markerField,
        string markerValue)
    {
        var registration = new RecordingEndpointRegistrationService();
        using var configuredFactory = CreateFactory(registration);
        using var client = CreateClient(configuredFactory, false, ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);
        var fields = ValidFields(mode);
        fields["Url"] = string.Empty;
        fields[markerField] = markerValue;

        using var response = await PostAsync(client, token, fields);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        AssertPlacement(content, mode);
        Assert.Contains("id=\"ajax-form-region\"", content, StringComparison.Ordinal);
        Assert.Contains("data-shell-validation-summary", content, StringComparison.Ordinal);
        Assert.Contains(markerValue, content, StringComparison.Ordinal);
        Assert.Empty(registration.Requests);
    }

    [Fact]
    public async Task AdvancedSettingsRoundTripThroughValidation()
    {
        var registration = new RecordingEndpointRegistrationService();
        using var configuredFactory = CreateFactory(registration);
        using var client = CreateClient(configuredFactory, false, ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);
        var fields = ValidFields(EndpointRegistrationModes.NewClient);
        fields["Url"] = string.Empty;
        fields["AdvancedSettingsOpen"] = "true";
        fields["IntervalMinutesOverride"] = "37";
        fields["WarningThresholdMsOverride"] = "1700";
        fields["CriticalThresholdMsOverride"] = "3400";

        using var response = await PostAsync(client, token, fields);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Matches("<details[^>]*data-registration-advanced[^>]* open", content);
        Assert.Contains("value=\"37\"", content, StringComparison.Ordinal);
        Assert.Contains("value=\"1700\"", content, StringComparison.Ordinal);
        Assert.Contains("value=\"3400\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdvancedSettingsOpenWhenAnAdvancedFieldIsInvalid()
    {
        var registration = new RecordingEndpointRegistrationService();
        using var configuredFactory = CreateFactory(registration);
        using var client = CreateClient(configuredFactory, false, ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);
        var fields = ValidFields(EndpointRegistrationModes.ExistingEnvironment);
        fields["AdvancedSettingsOpen"] = "false";
        fields["IntervalMinutesOverride"] = "0";

        using var response = await PostAsync(client, token, fields);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Matches("<details[^>]*data-registration-advanced[^>]* open", content);
        Assert.Empty(registration.Requests);
    }

    [Fact]
    public async Task ControllerMapsAllModesToClosedCommandsAndDiscardsHigherIds()
    {
        var registration = new RecordingEndpointRegistrationService();
        using var configuredFactory = CreateFactory(registration);
        using var client = CreateClient(configuredFactory, false, ApplicationRoles.Administrator);
        client.DefaultRequestHeaders.Add(AjaxResponseHeaders.Request, "1");
        var token = await GetAntiforgeryTokenAsync(client);

        foreach (var mode in EndpointRegistrationModes.All)
        {
            using var response = await PostAsync(client, token, ValidFields(mode));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Collection(
            registration.Requests,
            request => Assert.Equal(
                RegistrationFormReader.EnvironmentId,
                Assert.IsType<ExistingEnvironment>(request.Hierarchy).EnvironmentId),
            request => Assert.Equal(
                RegistrationFormReader.WebsiteId,
                Assert.IsType<NewEnvironment>(request.Hierarchy).WebsiteId),
            request => Assert.Equal(
                RegistrationFormReader.ClientId,
                Assert.IsType<NewWebsite>(request.Hierarchy).ClientId),
            request => Assert.IsType<NewClient>(request.Hierarchy));
    }

    [Fact]
    public async Task EndpointInventoryShowsTheGlobalRegistrationActionOnlyToManagers()
    {
        using var administrator = factory.CreateHttpsClient(ApplicationRoles.Administrator);
        using var viewer = factory.CreateHttpsClient(ApplicationRoles.Viewer);

        using var administratorResponse = await administrator.GetAsync("/Targets/Endpoints");
        using var viewerResponse = await viewer.GetAsync("/Targets/Endpoints");
        var administratorContent = await administratorResponse.Content.ReadAsStringAsync();
        var viewerContent = await viewerResponse.Content.ReadAsStringAsync();

        Assert.True(administratorResponse.IsSuccessStatusCode, administratorContent);
        Assert.True(viewerResponse.IsSuccessStatusCode, viewerContent);
        Assert.Contains("Register endpoint", administratorContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Register endpoint", viewerContent, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateFactory(RecordingEndpointRegistrationService registration) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            var readers = new RegistrationFormReader();
            services.RemoveAll<IRegistryReader>();
            services.RemoveAll<ITargetRegistryReader>();
            services.RemoveAll<IEndpointRegistrationService>();
            services.AddSingleton<IRegistryReader>(readers);
            services.AddSingleton<ITargetRegistryReader>(readers);
            services.AddSingleton<IEndpointRegistrationService>(registration);
        }));

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> configuredFactory,
        bool allowAutoRedirect,
        params string[] roles)
    {
        var client = configuredFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.HeaderName, "Test User");
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeaderName, string.Join(',', roles));
        return client;
    }

    private static Dictionary<string, string> ValidFields(string mode) => new()
    {
        ["ClientId"] = mode == EndpointRegistrationModes.NewClient
            ? CreateNew
            : RegistrationFormReader.ClientId.ToString(),
        ["WebsiteId"] = mode == EndpointRegistrationModes.NewWebsite
            ? CreateNew
            : RegistrationFormReader.WebsiteId.ToString(),
        ["EnvironmentId"] = mode == EndpointRegistrationModes.NewEnvironment
            ? CreateNew
            : RegistrationFormReader.EnvironmentId.ToString(),
        ["ClientName"] = "New client",
        ["ClientOwnerSubjectId"] = RegistrationFormReader.OwnerId.ToString(),
        ["WebsiteName"] = "New website",
        ["WebsiteOwnerSubjectId"] = RegistrationFormReader.OwnerId.ToString(),
        ["EnvironmentName"] = "Production",
        ["EnvironmentType"] = EnvironmentTypes.Production,
        ["EnvironmentBaseUrl"] = "https://new.example.test/",
        ["Url"] = "https://new.example.test/health",
        ["IsEnabled"] = "true",
        ["SchedulingEnabled"] = "true",
        ["SeoIndexingExpectation"] = "Default",
        ["SeoDescriptionRequired"] = "true",
        ["PageAuditIntervalHours"] = PageAuditCadence.DefaultIntervalHours.ToString()
    };

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var ajaxValues = client.DefaultRequestHeaders.GetValues(AjaxResponseHeaders.Request).ToArray();
        client.DefaultRequestHeaders.Remove(AjaxResponseHeaders.Request);
        try
        {
            var content = await client.GetStringAsync("/Targets/RegisterEndpoint");
            var match = Regex.Match(
                content,
                "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"",
                RegexOptions.CultureInvariant);
            Assert.True(match.Success, content);
            return WebUtility.HtmlDecode(match.Groups["token"].Value);
        }
        finally
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(AjaxResponseHeaders.Request, ajaxValues);
        }
    }

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string token,
        IReadOnlyDictionary<string, string> values)
    {
        var fields = values.ToDictionary(pair => pair.Key, pair => pair.Value);
        fields["__RequestVerificationToken"] = token;
        return client.PostAsync("/Targets/RegisterEndpoint", new FormUrlEncodedContent(fields));
    }

    private static string CreateNew { get; } = EndpointRegistrationFormViewModel.CreateNew.ToString();

    private static void AssertPlacement(string content, string mode)
    {
        AssertSelected(
            content,
            "ClientId",
            mode == EndpointRegistrationModes.NewClient ? CreateNew : RegistrationFormReader.ClientId.ToString());
        if (mode == EndpointRegistrationModes.NewClient)
        {
            return;
        }

        AssertSelected(
            content,
            "WebsiteId",
            mode == EndpointRegistrationModes.NewWebsite ? CreateNew : RegistrationFormReader.WebsiteId.ToString());
        if (mode == EndpointRegistrationModes.NewWebsite)
        {
            return;
        }

        AssertSelected(
            content,
            "EnvironmentId",
            mode == EndpointRegistrationModes.NewEnvironment
                ? CreateNew
                : RegistrationFormReader.EnvironmentId.ToString());
    }

    private static void AssertSelected(string content, string field, string value)
    {
        var select = Regex.Match(
            content,
            $"<select[^>]*name=\"{field}\"[^>]*>(?<options>.*?)</select>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(select.Success, $"No {field} select in: {content}");
        Assert.Matches(
            $"<option(?=[^>]*value=\"{Regex.Escape(value)}\")(?=[^>]*selected=\"selected\")[^>]*>",
            select.Groups["options"].Value);
    }

    private sealed class RecordingEndpointRegistrationService : IEndpointRegistrationService
    {
        public List<RegisterEndpointRequest> Requests { get; } = [];

        public Task<RegistryMutationResult> RegisterAsync(
            RegisterEndpointRequest request,
            RegistryAccessContext access,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(RegistryMutationResult.Success(Guid.NewGuid()));
        }
    }

    private sealed class RegistrationFormReader : IRegistryReader, ITargetRegistryReader
    {
        public static Guid OwnerId { get; } = Guid.Parse("7f1c9a20-0000-0000-0000-000000000001");
        public static Guid ClientId { get; } = Guid.Parse("7f1c9a20-0000-0000-0000-000000000002");
        public static Guid WebsiteId { get; } = Guid.Parse("7f1c9a20-0000-0000-0000-000000000003");
        public static Guid EmptyWebsiteId { get; } = Guid.Parse("7f1c9a20-0000-0000-0000-000000000004");
        public static Guid EnvironmentId { get; } = Guid.Parse("7f1c9a20-0000-0000-0000-000000000005");

        private static ClientListItem Client { get; } = new(
            ClientId, "Example client", "Example owner", true, false, 1, 2);

        private static WebsiteListItem Website { get; } = new(
            WebsiteId, ClientId, Client.Name, "Example website", "Example owner", null,
            true, false, 1, 1, []);

        private static WebsiteListItem EmptyWebsite { get; } = new(
            EmptyWebsiteId, ClientId, Client.Name, "Website with no environments", "Example owner", null,
            false, false, 1, 0, []);

        private static EnvironmentListItem Environment { get; } = new(
            EnvironmentId, WebsiteId, Client.Name, Website.Name, "Production", EnvironmentTypes.Production,
            true, "https://example.test/", true, false, 1, 0);

        public Task<IReadOnlyList<ClientListItem>> ListClientsAsync(
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Result<ClientListItem>(Client);

        public Task<IReadOnlyList<ClientListItem>> ListDeletedClientsAsync(
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Result<ClientListItem>();

        public Task<ClientDetails?> FindClientAsync(
            Guid clientId,
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Task.FromResult<ClientDetails?>(null);

        public Task<IReadOnlyList<WebsiteListItem>> ListWebsitesAsync(
            RegistryAccessContext access,
            Guid? tagId = null,
            CancellationToken cancellationToken = default) => Result(Website, EmptyWebsite);

        public Task<IReadOnlyList<RegistryTagOption>> ListTagsAsync(
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Result<RegistryTagOption>();

        public Task<IReadOnlyList<WebsiteListItem>> ListDeletedWebsitesAsync(
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Result<WebsiteListItem>();

        public Task<WebsiteDetails?> FindWebsiteAsync(
            Guid websiteId,
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Task.FromResult<WebsiteDetails?>(null);

        public Task<IReadOnlyList<RegistryOwnerOption>> ListOwnersAsync(
            Guid? includeOwnerSubjectId = null,
            CancellationToken cancellationToken = default) =>
            Result(new RegistryOwnerOption(OwnerId, "Example owner", "User"));

        public Task<IReadOnlyList<EnvironmentListItem>> ListEnvironmentsAsync(
            Guid websiteId,
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) =>
            Result<EnvironmentListItem>(websiteId == WebsiteId ? Environment : null);

        public Task<EnvironmentDetails?> FindEnvironmentAsync(
            Guid environmentId,
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Task.FromResult<EnvironmentDetails?>(null);

        public Task<IReadOnlyList<EndpointListItem>> ListEndpointsAsync(
            Guid environmentId,
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Result<EndpointListItem>();

        public Task<IReadOnlyList<RegistryEndpointItem>> ListAllEndpointsAsync(
            RegistryAccessContext access,
            EndpointRegistryFilter? filter = null,
            bool includeArchived = false,
            CancellationToken cancellationToken = default) => Result<RegistryEndpointItem>();

        public Task<IReadOnlyList<EnvironmentListItem>> ListAllEnvironmentsAsync(
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Result<EnvironmentListItem>(Environment);

        public Task<EndpointDetails?> FindEndpointAsync(
            Guid endpointId,
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Task.FromResult<EndpointDetails?>(null);

        public Task<CertificateStatus?> FindCertificateStatusAsync(
            Guid endpointId,
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Task.FromResult<CertificateStatus?>(null);

        public Task<IReadOnlyList<EnvironmentListItem>> ListDeletedEnvironmentsAsync(
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Result<EnvironmentListItem>();

        public Task<IReadOnlyList<EndpointListItem>> ListDeletedEndpointsAsync(
            RegistryAccessContext access,
            CancellationToken cancellationToken = default) => Result<EndpointListItem>();

        private static Task<IReadOnlyList<T>> Result<T>(params T?[] values) =>
            Task.FromResult<IReadOnlyList<T>>(values.Where(value => value is not null).Cast<T>().ToArray());
    }
}
