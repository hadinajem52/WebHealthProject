using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Application.Monitoring;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Identity;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Registry;

namespace WebHealth.IntegrationTests.Support;

internal static class EndpointRegistrationAssertions
{
    public static async Task VerifyAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebHealth"] = connectionString
        }).Build();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var registration = scope.ServiceProvider.GetRequiredService<IEndpointRegistrationService>();
        var eligibility = scope.ServiceProvider.GetRequiredService<IMonitoringEligibilityService>();
        var authorization = scope.ServiceProvider.GetRequiredService<ITargetAuthorizationService>();
        scope.ServiceProvider.GetRequiredService<IClientRegistryService>().Should()
            .BeSameAs(scope.ServiceProvider.GetRequiredService<ClientRegistryService>());
        scope.ServiceProvider.GetRequiredService<IWebsiteRegistryService>().Should()
            .BeSameAs(scope.ServiceProvider.GetRequiredService<WebsiteRegistryService>());
        scope.ServiceProvider.GetRequiredService<IEnvironmentRegistryService>().Should()
            .BeSameAs(scope.ServiceProvider.GetRequiredService<EnvironmentRegistryService>());
        scope.ServiceProvider.GetRequiredService<IEndpointRegistryService>().Should()
            .BeSameAs(scope.ServiceProvider.GetRequiredService<EndpointRegistryService>());

        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var administratorOwnerId = await OwnerIdAsync(database, administrator.Id);
        var disabledUser = await database.Users.SingleAsync(user => user.Email == "managed-viewer@example.test");
        disabledUser.IsDisabled.Should().BeTrue();
        var disabledOwnerId = await OwnerIdAsync(database, disabledUser.Id);
        var access = new RegistryAccessContext(administrator.Id, [ApplicationRoles.Administrator]);
        var label = $"registration-{Guid.NewGuid():N}";

        await VerifyAuthorizationAsync(registration, database, administrator.Id, administratorOwnerId, label);

        var modeFour = await RegisterAsync(
            registration,
            new NewClient(
                new($"Client {label}", administratorOwnerId, "Phase 3 mode four"),
                Website($"Website {label}", administratorOwnerId),
                Environment($"Production {label}", label)),
            Settings($"https://{label}.example.test/health"),
            access);
        var baseEndpoint = await LoadEndpointAsync(database, modeFour);
        baseEndpoint.Environment.Website.IsEnabled.Should().BeTrue();
        baseEndpoint.Environment.IsActive.Should().BeTrue();
        (await eligibility.IsEndpointEligibleAsync(modeFour)).Should().BeTrue();

        var modeOne = await RegisterAsync(
            registration,
            new ExistingEnvironment(baseEndpoint.EnvironmentId),
            Settings($"https://{label}.example.test/mode-one"),
            access);
        (await eligibility.IsEndpointEligibleAsync(modeOne)).Should().BeTrue();

        var modeTwo = await RegisterAsync(
            registration,
            new NewEnvironment(
                baseEndpoint.Environment.WebsiteId,
                Environment($"Staging {label}", $"staging-{label}")),
            Settings($"https://staging-{label}.example.test/health"),
            access);
        var modeTwoEndpoint = await LoadEndpointAsync(database, modeTwo);
        modeTwoEndpoint.Environment.Website.ClientId
            .Should().Be(baseEndpoint.Environment.Website.ClientId);
        (await eligibility.IsEndpointEligibleAsync(modeTwo)).Should().BeTrue();

        var modeThree = await RegisterAsync(
            registration,
            new NewWebsite(
                baseEndpoint.Environment.Website.ClientId,
                Website($"Second website {label}", administratorOwnerId),
                Environment($"Production second {label}", $"second-{label}")),
            Settings($"https://second-{label}.example.test/health"),
            access);
        var modeThreeEndpoint = await LoadEndpointAsync(database, modeThree);
        modeThreeEndpoint.Environment.Website.ClientId
            .Should().Be(baseEndpoint.Environment.Website.ClientId);
        modeThreeEndpoint.Environment.Website.IsEnabled.Should().BeTrue();
        (await eligibility.IsEndpointEligibleAsync(modeThree)).Should().BeTrue();

        await VerifyDuplicateEndpointAsync(registration, baseEndpoint, access);
        await VerifyDuplicateWebsiteAsync(registration, baseEndpoint, administratorOwnerId, label, access);
        await VerifyRollbackAsync(registration, database, administratorOwnerId, label, access);
        await VerifyUnreachableHostAsync(registration, database, administratorOwnerId, label, access);
        await VerifyInactiveEnvironmentAsync(registration, database, modeTwoEndpoint, label, access);
        await VerifyDisabledOwnerAsync(registration, baseEndpoint, disabledOwnerId, label, access);
        await VerifyMissingAuthorizationAsync(
            registration,
            database,
            eligibility,
            baseEndpoint,
            label,
            access);
        await VerifyDisabledWebsiteAsync(registration, database, eligibility, baseEndpoint, label, access);
        await VerifyInactiveClientAsync(registration, database, eligibility, baseEndpoint, label, access);
        await VerifyManualOnlyAsync(
            registration,
            eligibility,
            authorization,
            baseEndpoint,
            label,
            access);
    }

    private static async Task VerifyAuthorizationAsync(
        IEndpointRegistrationService registration,
        ApplicationDbContext database,
        Guid userId,
        Guid ownerSubjectId,
        string label)
    {
        foreach (var role in new[] { ApplicationRoles.DeveloperSupport, ApplicationRoles.Viewer })
        {
            var clientName = $"Forbidden {role} {label}";
            var result = await registration.RegisterAsync(
                new RegisterEndpointRequest(
                    new NewClient(
                        new(clientName, ownerSubjectId, null),
                        Website($"Forbidden website {role} {label}", ownerSubjectId),
                        Environment($"Forbidden environment {role} {label}", $"forbidden-{role}-{label}")),
                    Settings($"https://forbidden-{role}-{label}.example.test/")),
                new RegistryAccessContext(userId, [role]));

            result.Status.Should().Be(RegistryMutationStatus.Forbidden);
            (await database.Clients.AnyAsync(client => client.Name == clientName)).Should().BeFalse();
        }
    }

    private static async Task VerifyDuplicateEndpointAsync(
        IEndpointRegistrationService registration,
        Endpoint endpoint,
        RegistryAccessContext access)
    {
        var uri = new Uri(endpoint.NormalizedUrl);
        var result = await registration.RegisterAsync(
            new RegisterEndpointRequest(
                new ExistingEnvironment(endpoint.EnvironmentId),
                Settings($" HTTPS://{uri.Host.ToUpperInvariant()}:443{uri.PathAndQuery} ")),
            access);

        AssertField(result, EndpointRegistrationFields.Url)
            .Should().Contain("already has an endpoint");
    }

    private static async Task VerifyDuplicateWebsiteAsync(
        IEndpointRegistrationService registration,
        Endpoint endpoint,
        Guid ownerSubjectId,
        string label,
        RegistryAccessContext access)
    {
        var result = await registration.RegisterAsync(
            new RegisterEndpointRequest(
                new NewWebsite(
                    endpoint.Environment.Website.ClientId,
                    Website(endpoint.Environment.Website.Name.ToUpperInvariant(), ownerSubjectId),
                    Environment($"Duplicate website environment {label}", $"duplicate-website-{label}")),
                Settings($"https://duplicate-website-{label}.example.test/")),
            access);

        AssertField(result, EndpointRegistrationFields.WebsiteName)
            .Should().Contain("already has an active website");
    }

    private static async Task VerifyRollbackAsync(
        IEndpointRegistrationService registration,
        ApplicationDbContext database,
        Guid ownerSubjectId,
        string label,
        RegistryAccessContext access)
    {
        var clientName = $"Rollback client {label}";
        var counts = await CountsAsync(database);
        var result = await registration.RegisterAsync(
            new RegisterEndpointRequest(
                new NewClient(
                    new(clientName, ownerSubjectId, null),
                    Website($"Rollback website {label}", ownerSubjectId),
                    Environment($"Rollback environment {label}", $"rollback-{label}")),
                Settings("/relative")),
            access);

        AssertField(result, EndpointRegistrationFields.Url).Should().Contain("absolute");
        (await CountsAsync(database)).Should().Be(counts);
        (await database.Clients.AnyAsync(client => client.Name == clientName)).Should().BeFalse();
    }

    private static async Task VerifyUnreachableHostAsync(
        IEndpointRegistrationService registration,
        ApplicationDbContext database,
        Guid ownerSubjectId,
        string label,
        RegistryAccessContext access)
    {
        var counts = await CountsAsync(database);
        var result = await registration.RegisterAsync(
            new RegisterEndpointRequest(
                new NewClient(
                    new($"Unreachable client {label}", ownerSubjectId, null),
                    Website($"Unreachable website {label}", ownerSubjectId),
                    Environment($"Unreachable environment {label}", $"unreachable-{label}")),
                Settings("https://127.0.0.1/")),
            access);

        AssertField(result, EndpointRegistrationFields.Url).Should().NotBeNullOrWhiteSpace();
        (await CountsAsync(database)).Should().Be(counts);
    }

    private static async Task VerifyInactiveEnvironmentAsync(
        IEndpointRegistrationService registration,
        ApplicationDbContext database,
        Endpoint endpoint,
        string label,
        RegistryAccessContext access)
    {
        var environment = await database.Environments.SingleAsync(item => item.Id == endpoint.EnvironmentId);
        environment.IsActive = false;
        environment.Version++;
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var result = await registration.RegisterAsync(
            new RegisterEndpointRequest(
                new ExistingEnvironment(endpoint.EnvironmentId),
                Settings($"https://inactive-environment-{label}.example.test/")),
            access);

        AssertField(result, EndpointRegistrationFields.EnvironmentId)
            .Should().Contain(environment.Name);
    }

    private static async Task VerifyDisabledOwnerAsync(
        IEndpointRegistrationService registration,
        Endpoint endpoint,
        Guid disabledOwnerId,
        string label,
        RegistryAccessContext access)
    {
        var result = await registration.RegisterAsync(
            new RegisterEndpointRequest(
                new ExistingEnvironment(endpoint.EnvironmentId),
                Settings($"https://disabled-owner-{label}.example.test/", ownerSubjectId: disabledOwnerId)),
            access);

        AssertField(result, EndpointRegistrationFields.OwnerSubjectId)
            .Should().Contain("disabled owner");
    }

    private static async Task VerifyMissingAuthorizationAsync(
        IEndpointRegistrationService registration,
        ApplicationDbContext database,
        IMonitoringEligibilityService eligibility,
        Endpoint endpoint,
        string label,
        RegistryAccessContext access)
    {
        var rejected = await registration.RegisterAsync(
            new RegisterEndpointRequest(
                new ExistingEnvironment(endpoint.EnvironmentId),
                Settings($"https://missing-evidence-{label}.example.test/", evidence: false)),
            access);
        AssertField(rejected, EndpointRegistrationFields.TargetAuthorizationEvidence)
            .Should().Contain("needs testing evidence");

        var disabledId = await RegisterAsync(
            registration,
            new ExistingEnvironment(endpoint.EnvironmentId),
            Settings(
                $"https://disabled-no-evidence-{label}.example.test/",
                enabled: false,
                evidence: false),
            access);
        var disabled = await LoadEndpointAsync(database, disabledId);
        disabled.IsEnabled.Should().BeFalse();
        (await database.TargetAuthorizations.AnyAsync(item => item.EndpointId == disabledId))
            .Should().BeFalse();
        (await eligibility.IsEndpointEligibleAsync(disabledId)).Should().BeFalse();
    }

    private static async Task VerifyDisabledWebsiteAsync(
        IEndpointRegistrationService registration,
        ApplicationDbContext database,
        IMonitoringEligibilityService eligibility,
        Endpoint endpoint,
        string label,
        RegistryAccessContext access)
    {
        var website = await database.Websites.SingleAsync(item => item.Id == endpoint.Environment.WebsiteId);
        website.IsEnabled = false;
        website.Version++;
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var rejected = await registration.RegisterAsync(
            new RegisterEndpointRequest(
                new ExistingEnvironment(endpoint.EnvironmentId),
                Settings($"https://disabled-website-rejected-{label}.example.test/")),
            access);
        AssertField(rejected, EndpointRegistrationFields.WebsiteId).Should().Contain(website.Name);

        var disabledId = await RegisterAsync(
            registration,
            new ExistingEnvironment(endpoint.EnvironmentId),
            Settings($"https://disabled-website-{label}.example.test/", enabled: false, evidence: false),
            access);
        var disabled = await LoadEndpointAsync(database, disabledId);
        disabled.IsEnabled.Should().BeFalse();
        (await eligibility.IsEndpointEligibleAsync(disabledId)).Should().BeFalse();

        website = await database.Websites.SingleAsync(item => item.Id == endpoint.Environment.WebsiteId);
        website.IsEnabled = true;
        website.Version++;
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
    }

    private static async Task VerifyInactiveClientAsync(
        IEndpointRegistrationService registration,
        ApplicationDbContext database,
        IMonitoringEligibilityService eligibility,
        Endpoint endpoint,
        string label,
        RegistryAccessContext access)
    {
        var client = await database.Clients.SingleAsync(item => item.Id == endpoint.Environment.Website.ClientId);
        client.IsActive = false;
        client.Version++;
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var rejected = await registration.RegisterAsync(
            new RegisterEndpointRequest(
                new ExistingEnvironment(endpoint.EnvironmentId),
                Settings($"https://inactive-client-rejected-{label}.example.test/")),
            access);
        AssertField(rejected, EndpointRegistrationFields.ClientId).Should().Contain(client.Name);

        var disabledId = await RegisterAsync(
            registration,
            new ExistingEnvironment(endpoint.EnvironmentId),
            Settings($"https://inactive-client-{label}.example.test/", enabled: false, evidence: false),
            access);
        var disabled = await LoadEndpointAsync(database, disabledId);
        disabled.IsEnabled.Should().BeFalse();
        (await eligibility.IsEndpointEligibleAsync(disabledId)).Should().BeFalse();

        var disabledWithNewWebsiteId = await RegisterAsync(
            registration,
            new NewWebsite(
                client.Id,
                Website($"Inactive client website {label}", endpoint.Environment.Website.OwnerSubjectId),
                Environment($"Inactive client environment {label}", $"inactive-client-new-{label}")),
            Settings(
                $"https://inactive-client-new-{label}.example.test/",
                enabled: false,
                evidence: false),
            access);
        var disabledWithNewWebsite = await LoadEndpointAsync(database, disabledWithNewWebsiteId);
        disabledWithNewWebsite.Environment.Website.IsEnabled.Should().BeTrue();
        disabledWithNewWebsite.IsEnabled.Should().BeFalse();
        (await eligibility.IsEndpointEligibleAsync(disabledWithNewWebsiteId)).Should().BeFalse();

        client = await database.Clients.SingleAsync(item => item.Id == endpoint.Environment.Website.ClientId);
        client.IsActive = true;
        client.Version++;
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
    }

    private static async Task VerifyManualOnlyAsync(
        IEndpointRegistrationService registration,
        IMonitoringEligibilityService eligibility,
        ITargetAuthorizationService authorization,
        Endpoint endpoint,
        string label,
        RegistryAccessContext access)
    {
        var endpointId = await RegisterAsync(
            registration,
            new ExistingEnvironment(endpoint.EnvironmentId),
            Settings($"https://manual-only-{label}.example.test/", scheduling: false),
            access);

        (await authorization.CanTestEndpointAsync(endpointId, access)).Should().BeTrue();
        (await eligibility.IsEndpointEligibleAsync(endpointId)).Should().BeFalse();
    }

    private static async Task<Guid> RegisterAsync(
        IEndpointRegistrationService registration,
        EndpointRegistrationHierarchy hierarchy,
        EndpointRegistrationSettings endpoint,
        RegistryAccessContext access)
    {
        var result = await registration.RegisterAsync(new(hierarchy, endpoint), access);
        result.Succeeded.Should().BeTrue(string.Join(" ", result.Errors));
        return result.EntityId!.Value;
    }

    private static EndpointRegistrationWebsite Website(string name, Guid ownerSubjectId) =>
        new(name, ownerSubjectId, null, []);

    private static EndpointRegistrationEnvironment Environment(string name, string host) =>
        new(name, EnvironmentTypes.Staging, $"https://{host}.example.test/");

    private static EndpointRegistrationSettings Settings(
        string url,
        bool enabled = true,
        bool scheduling = true,
        bool evidence = true,
        Guid? ownerSubjectId = null) => new()
        {
            Url = url,
            OwnerSubjectId = ownerSubjectId,
            IsEnabled = enabled,
            TargetAuthorizationKind = evidence ? TargetAuthorizationKinds.Owned : null,
            TargetAuthorizationEvidence = evidence
                ? "Phase 3 registration fixture owned by the project."
                : null,
            SchedulingEnabled = scheduling
        };

    private static async Task<Guid> OwnerIdAsync(ApplicationDbContext database, Guid userId) =>
        await database.OwnerSubjects.Where(owner => owner.UserId == userId)
            .Select(owner => owner.Id)
            .SingleAsync();

    private static async Task<Endpoint> LoadEndpointAsync(ApplicationDbContext database, Guid endpointId) =>
        await database.Endpoints.AsNoTracking()
            .Include(endpoint => endpoint.Environment)
            .ThenInclude(environment => environment.Website)
            .ThenInclude(website => website.Client)
            .SingleAsync(endpoint => endpoint.Id == endpointId);

    private static async Task<RegistrationCounts> CountsAsync(ApplicationDbContext database) => new(
        await database.Clients.CountAsync(),
        await database.Websites.CountAsync(),
        await database.Environments.CountAsync(),
        await database.Endpoints.CountAsync(),
        await database.EndpointMonitors.CountAsync(),
        await database.AuditEvents.CountAsync());

    private static string AssertField(RegistryMutationResult result, string field)
    {
        result.Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        return result.Errors.Should().ContainSingle(error => error.Field == field).Subject.Message;
    }

    private sealed record RegistrationCounts(
        int Clients,
        int Websites,
        int Environments,
        int Endpoints,
        int Monitors,
        int Audits);
}
