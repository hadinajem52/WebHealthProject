using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebHealth.Infrastructure;
using WebHealth.Infrastructure.Identity;
using Npgsql;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Application.Administration;
using WebHealth.Application.Auditing;
using WebHealth.Application.Assignments;
using WebHealth.Infrastructure.Assignments;
using WebHealth.Application.Registry;
using WebHealth.Infrastructure.Registry;
using Xunit;
using System.Text.Json;

namespace WebHealth.IntegrationTests.Support;

internal static class DatabaseFoundationAssertions
{
    private static readonly string[] ExpectedTables =
    [
        "audit_event",
        "access_grant",
        "client",
        "environment",
        "website",
        "app_role",
        "app_role_claim",
        "app_user",
        "app_user_claim",
        "app_user_login",
        "app_user_role",
        "app_user_token"
        ,"owner_subject"
        ,"team"
        ,"team_member"
    ];

    public static async Task VerifyAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        PostgreSqlDbContextOptions.Configure(options, connectionString);
        await using var context = new ApplicationDbContext(options.Options);

        await context.Database.MigrateAsync();

        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await context.Database.GetAppliedMigrationsAsync()).Should().HaveCount(5);
        context.Model.GetEntityTypes().Should().HaveCount(15);

        var state = await ReadFoundationState(connectionString);
        state.SchemaExists.Should().BeTrue();
        state.Tables.Should().BeEquivalentTo(
            ExpectedTables.Append(DatabaseConventions.MigrationsHistoryTable));

        await VerifyIdentityBootstrapAsync(connectionString);
        await VerifyClientWebsiteRegistryAsync(connectionString);
    }

    private static async Task VerifyClientWebsiteRegistryAsync(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WebHealth"] = connectionString
            })
            .Build();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
        var clients = scope.ServiceProvider.GetRequiredService<IClientRegistryService>();
        var websites = scope.ServiceProvider.GetRequiredService<IWebsiteRegistryService>();
        var reader = scope.ServiceProvider.GetRequiredService<IRegistryReader>();

        var administrator = await database.Users.SingleAsync(user => user.Email == "bootstrap@example.test");
        var administratorOwnerId = await database.OwnerSubjects
            .Where(owner => owner.UserId == administrator.Id)
            .Select(owner => owner.Id)
            .SingleAsync();
        var administratorAccess = new RegistryAccessContext(
            administrator.Id,
            [ApplicationRoles.Administrator]);

        var developerResult = await users.CreateUserAsync(
            new CreateManagedUser(
                "Registry Developer",
                "registry-developer@example.test",
                $"Registry-9!{Guid.NewGuid():N}",
                [ApplicationRoles.DeveloperSupport]),
            administrator.Id);
        developerResult.Succeeded.Should().BeTrue();
        var viewerResult = await users.CreateUserAsync(
            new CreateManagedUser(
                "Registry Viewer",
                "registry-viewer@example.test",
                $"Registry-9!{Guid.NewGuid():N}",
                [ApplicationRoles.Viewer]),
            administrator.Id);
        viewerResult.Succeeded.Should().BeTrue();

        var developer = await database.Users.SingleAsync(user => user.Email == "registry-developer@example.test");
        var viewer = await database.Users.SingleAsync(user => user.Email == "registry-viewer@example.test");
        var developerOwnerId = await database.OwnerSubjects
            .Where(owner => owner.UserId == developer.Id)
            .Select(owner => owner.Id)
            .SingleAsync();

        var firstClient = await clients.CreateAsync(
            new CreateClient("  Alpha Client  ", developerOwnerId, "  scoped notes  "),
            administratorAccess);
        firstClient.Succeeded.Should().BeTrue();
        var firstClientId = firstClient.EntityId ?? throw new InvalidOperationException("Client id was not returned.");
        var duplicateClient = await clients.CreateAsync(
            new CreateClient("alpha client", administratorOwnerId, null),
            administratorAccess);
        duplicateClient.Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        var secondClient = await clients.CreateAsync(
            new CreateClient("Second Client", administratorOwnerId, null),
            administratorAccess);
        secondClient.Succeeded.Should().BeTrue();
        var secondClientId = secondClient.EntityId ?? throw new InvalidOperationException("Client id was not returned.");

        var persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        persistedClient.Name.Should().Be("Alpha Client");
        persistedClient.Notes.Should().Be("scoped notes");
        var staleClientUpdate = await clients.UpdateAsync(
            new UpdateClient(
                persistedClient.Id,
                persistedClient.Name,
                persistedClient.OwnerSubjectId,
                persistedClient.Notes,
                true,
                0),
            administratorAccess);
        staleClientUpdate.Status.Should().Be(RegistryMutationStatus.ConcurrencyConflict);

        var firstWebsite = await websites.CreateAsync(
            new CreateWebsite(firstClientId, "  Portal  ", developerOwnerId, " ASP.NET ", false),
            administratorAccess);
        firstWebsite.Succeeded.Should().BeTrue();
        var firstWebsiteId = firstWebsite.EntityId ?? throw new InvalidOperationException("Website id was not returned.");
        (await websites.CreateAsync(
            new CreateWebsite(firstClientId, "portal", developerOwnerId, null, false),
            administratorAccess)).Status.Should().Be(RegistryMutationStatus.ValidationFailed);
        (await websites.CreateAsync(
            new CreateWebsite(secondClientId, "Portal", administratorOwnerId, null, false),
            administratorAccess)).Succeeded.Should().BeTrue();

        var persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var enableWithoutEnvironment = await websites.UpdateAsync(
            new UpdateWebsite(
                persistedWebsite.Id,
                persistedWebsite.Name,
                persistedWebsite.OwnerSubjectId,
                persistedWebsite.TechnologyCms,
                true,
                persistedWebsite.Version),
            administratorAccess);
        enableWithoutEnvironment.Status.Should().Be(RegistryMutationStatus.ValidationFailed);

        await VerifyEnabledWebsiteConstraintAsync(connectionString, persistedWebsite.Id);
        database.ChangeTracker.Clear();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var now = DateTimeOffset.UtcNow;
        database.Environments.Add(new WebsiteEnvironment
        {
            Id = Guid.NewGuid(),
            WebsiteId = persistedWebsite.Id,
            Name = "Production",
            NormalizedName = "production",
            NormalizationVersion = 1,
            EnvironmentType = "Production",
            IsProduction = true,
            BaseUrl = "https://example.test",
            IsActive = true,
            CreatedAt = now,
            CreatedByUserId = administrator.Id,
            UpdatedAt = now,
            UpdatedByUserId = administrator.Id,
            Version = 1
        });
        await database.SaveChangesAsync();
        (await websites.UpdateAsync(
            new UpdateWebsite(
                persistedWebsite.Id,
                persistedWebsite.Name,
                persistedWebsite.OwnerSubjectId,
                persistedWebsite.TechnologyCms,
                true,
                persistedWebsite.Version),
            administratorAccess)).Succeeded.Should().BeTrue();

        database.AccessGrants.Add(new AccessGrant
        {
            Id = Guid.NewGuid(),
            UserId = viewer.Id,
            AccessLevel = "Read",
            ClientId = firstClientId,
            EffectiveFrom = now.AddMinutes(-1),
            CreatedAt = now,
            CreatedByUserId = administrator.Id
        });
        await database.SaveChangesAsync();

        var developerClients = await reader.ListClientsAsync(new(
            developer.Id,
            [ApplicationRoles.DeveloperSupport]));
        developerClients.Select(client => client.Id).Should().Equal(firstClientId);
        var viewerWebsites = await reader.ListWebsitesAsync(new(viewer.Id, [ApplicationRoles.Viewer]));
        viewerWebsites.Should().Contain(website => website.Id == firstWebsiteId);
        viewerWebsites.Should().NotContain(website => website.ClientId == secondClientId);

        database.ChangeTracker.Clear();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var disabled = await websites.DisableAsync(new(persistedWebsite.Id, persistedWebsite.Version), administratorAccess);
        disabled.Succeeded.Should().BeTrue();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var deleted = await websites.DeleteAsync(new(persistedWebsite.Id, persistedWebsite.Version), administratorAccess);
        deleted.Succeeded.Should().BeTrue();
        (await reader.ListWebsitesAsync(administratorAccess))
            .Should().NotContain(website => website.Id == firstWebsiteId);
        (await reader.ListDeletedWebsitesAsync(administratorAccess))
            .Should().ContainSingle(website => website.Id == firstWebsiteId);
        (await reader.ListDeletedWebsitesAsync(new(developer.Id, [ApplicationRoles.DeveloperSupport])))
            .Should().BeEmpty();
        persistedWebsite = await database.Websites.SingleAsync(website => website.Id == firstWebsiteId);
        var restored = await websites.RestoreAsync(new(persistedWebsite.Id, persistedWebsite.Version), administratorAccess);
        restored.Succeeded.Should().BeTrue();

        var websiteAuditActions = await database.AuditEvents
            .Where(audit => audit.EntityIdentifier == persistedWebsite.Id.ToString())
            .Select(audit => audit.Action)
            .ToListAsync();
        websiteAuditActions.Should().Contain([
            "website.created",
            "website.updated",
            "website.disabled",
            "website.deleted",
            "website.restored"]);

        database.ChangeTracker.Clear();
        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        (await clients.UpdateAsync(
            new UpdateClient(
                persistedClient.Id,
                persistedClient.Name,
                persistedClient.OwnerSubjectId,
                "changed private notes",
                true,
                persistedClient.Version),
            administratorAccess)).Succeeded.Should().BeTrue();
        var notesAuditJson = await database.AuditEvents
            .Where(audit => audit.EntityIdentifier == firstClientId.ToString()
                && audit.Action == "client.updated")
            .OrderByDescending(audit => audit.OccurredAt)
            .Select(audit => audit.AfterValues)
            .FirstAsync();
        using (var notesAudit = JsonDocument.Parse(notesAuditJson!))
        {
            notesAudit.RootElement.GetProperty("notesChanged").GetBoolean().Should().BeTrue();
            notesAuditJson.Should().NotContain("changed private notes");
        }

        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        (await clients.DisableAsync(new(persistedClient.Id, persistedClient.Version), administratorAccess))
            .Succeeded.Should().BeTrue();
        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        (await clients.DeleteAsync(new(persistedClient.Id, persistedClient.Version), administratorAccess))
            .Succeeded.Should().BeTrue();
        (await reader.ListClientsAsync(administratorAccess))
            .Should().NotContain(client => client.Id == firstClientId);
        (await reader.ListDeletedClientsAsync(administratorAccess))
            .Should().ContainSingle(client => client.Id == firstClientId);
        (await reader.ListDeletedClientsAsync(new(viewer.Id, [ApplicationRoles.Viewer])))
            .Should().BeEmpty();
        persistedClient = await database.Clients.SingleAsync(client => client.Id == firstClientId);
        (await clients.RestoreAsync(new(persistedClient.Id, persistedClient.Version), administratorAccess))
            .Succeeded.Should().BeTrue();

        var clientAuditActions = await database.AuditEvents
            .Where(audit => audit.EntityIdentifier == firstClientId.ToString())
            .Select(audit => audit.Action)
            .ToListAsync();
        clientAuditActions.Should().Contain([
            "client.created",
            "client.updated",
            "client.disabled",
            "client.deleted",
            "client.restored"]);
    }

    private static async Task VerifyEnabledWebsiteConstraintAsync(
        string connectionString,
        Guid websiteId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE web_health.website SET is_enabled = TRUE WHERE id = @id",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", websiteId);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.Should().Be("ck_website_enabled_requires_active_environment");
    }

    private static async Task VerifyIdentityBootstrapAsync(string connectionString)
    {
        var password = $"Integration-9!{Guid.NewGuid():N}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WebHealth"] = connectionString,
                ["BootstrapAdmin:Email"] = "bootstrap@example.test",
                ["BootstrapAdmin:DisplayName"] = "Bootstrap Administrator",
                ["BootstrapAdmin:Password"] = password
            })
            .Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();
            await bootstrapper.BootstrapAsync();
            await bootstrapper.BootstrapAsync();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var roles = await roleManager.Roles.OrderBy(role => role.Name).ToListAsync();
            roles.Select(role => (role.Id, role.Name)).Should().BeEquivalentTo(
                ApplicationRoles.All.Select(role => (role.Id, (string?)role.Name)));

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync("bootstrap@example.test");
            user.Should().NotBeNull();
            user!.PasswordHash.Should().NotBeNullOrWhiteSpace().And.NotBe(password);
            (await userManager.CheckPasswordAsync(user, password)).Should().BeTrue();
            (await userManager.IsInRoleAsync(user, ApplicationRoles.Administrator)).Should().BeTrue();

            var signInManager = scope.ServiceProvider
                .GetRequiredService<SignInManager<ApplicationUser>>();
            user.IsDisabled = true;
            (await signInManager.CanSignInAsync(user)).Should().BeFalse();
            user.IsDisabled = false;

            var administration = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
            var managedPassword = $"Managed-8!{Guid.NewGuid():N}";
            var createResult = await administration.CreateUserAsync(
                new CreateManagedUser(
                    "Managed Viewer",
                    "managed-viewer@example.test",
                    managedPassword,
                    [ApplicationRoles.Viewer]),
                user.Id);
            createResult.Succeeded.Should().BeTrue();

            var managedUser = await userManager.FindByIdAsync(createResult.UserId!.Value.ToString());
            managedUser.Should().NotBeNull();
            var originalSecurityStamp = managedUser!.SecurityStamp;
            var existingPrincipal = await signInManager.CreateUserPrincipalAsync(managedUser);
            var replacementPassword = $"Replacement-7!{Guid.NewGuid():N}";

            var disableResult = await administration.UpdateUserAsync(
                new UpdateManagedUser(
                    managedUser.Id,
                    "Managed Viewer",
                    true,
                    [ApplicationRoles.Operations],
                    replacementPassword),
                user.Id);
            disableResult.Succeeded.Should().BeTrue();

            managedUser = await userManager.FindByIdAsync(managedUser.Id.ToString());
            managedUser!.IsDisabled.Should().BeTrue();
            managedUser.SecurityStamp.Should().NotBe(originalSecurityStamp);
            (await userManager.GetRolesAsync(managedUser)).Should().Equal(ApplicationRoles.Operations);
            (await userManager.CheckPasswordAsync(managedUser, replacementPassword)).Should().BeTrue();
            managedUser.PasswordHash.Should().NotBe(replacementPassword);
            (await signInManager.ValidateSecurityStampAsync(existingPrincipal)).Should().BeNull();

            var selfDisableResult = await administration.UpdateUserAsync(
                new UpdateManagedUser(
                    user.Id,
                    user.DisplayName,
                    true,
                    [ApplicationRoles.Administrator]),
                user.Id);
            selfDisableResult.Succeeded.Should().BeFalse();

            var roleOnlyPassword = $"Role-only-6!{Guid.NewGuid():N}";
            var roleOnlyCreateResult = await administration.CreateUserAsync(
                new CreateManagedUser(
                    "Role-only Administrator",
                    "role-only@example.test",
                    roleOnlyPassword,
                    [ApplicationRoles.Administrator]),
                user.Id);
            roleOnlyCreateResult.Succeeded.Should().BeTrue();

            var roleOnlyUser = await userManager.FindByIdAsync(roleOnlyCreateResult.UserId!.Value.ToString());
            var roleOnlyPrincipal = await signInManager.CreateUserPrincipalAsync(roleOnlyUser!);
            var roleOnlySecurityStamp = roleOnlyUser!.SecurityStamp;
            var roleOnlyUpdateResult = await administration.UpdateUserAsync(
                new UpdateManagedUser(
                    roleOnlyUser.Id,
                    roleOnlyUser.DisplayName,
                    false,
                    [ApplicationRoles.Viewer]),
                user.Id);
            roleOnlyUpdateResult.Succeeded.Should().BeTrue();

            roleOnlyUser = await userManager.FindByIdAsync(roleOnlyUser.Id.ToString());
            roleOnlyUser!.SecurityStamp.Should().NotBe(roleOnlySecurityStamp);
            (await signInManager.ValidateSecurityStampAsync(roleOnlyPrincipal)).Should().BeNull();

            var teamAdministration = scope.ServiceProvider.GetRequiredService<ITeamAdministrationService>();
            var createTeamResult = await teamAdministration.CreateTeamAsync(
                new CreateManagedTeam("  Platform   Support  ", [roleOnlyUser.Id]),
                user.Id);
            createTeamResult.Succeeded.Should().BeTrue();
            var team = await teamAdministration.FindTeamAsync(createTeamResult.TeamId!.Value);
            team.Should().NotBeNull();
            team!.Name.Should().Be("Platform Support");
            team.Members.Select(member => member.UserId).Should().Equal(roleOnlyUser.Id);
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var teamOwnerSubjectId = await database.OwnerSubjects
                .Where(subject => subject.TeamId == team.Id)
                .Select(subject => subject.Id)
                .SingleAsync();
            var assignmentAccess = scope.ServiceProvider.GetRequiredService<IAssignmentAccessEvaluator>();
            (await assignmentAccess.IsAssignedAsync(
                roleOnlyUser.Id,
                teamOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeTrue();

            var duplicateTeamResult = await teamAdministration.CreateTeamAsync(
                new CreateManagedTeam("platform support", []),
                user.Id);
            duplicateTeamResult.Succeeded.Should().BeFalse();

            var disabledTeamResult = await teamAdministration.CreateTeamAsync(
                new CreateManagedTeam("Disabled Team", [roleOnlyUser.Id]),
                user.Id);
            disabledTeamResult.Succeeded.Should().BeTrue();
            var disabledTeam = await teamAdministration.FindTeamAsync(disabledTeamResult.TeamId!.Value);
            var disabledTeamOwnerSubjectId = await database.OwnerSubjects
                .Where(subject => subject.TeamId == disabledTeam!.Id)
                .Select(subject => subject.Id)
                .SingleAsync();
            (await teamAdministration.UpdateTeamAsync(
                new UpdateManagedTeam(
                    disabledTeam!.Id,
                    disabledTeam.Name,
                    true,
                    disabledTeam.Version,
                    [roleOnlyUser.Id]),
                user.Id)).Succeeded.Should().BeTrue();
            (await assignmentAccess.IsAssignedAsync(
                roleOnlyUser.Id,
                disabledTeamOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeFalse();

            var updateTeamResult = await teamAdministration.UpdateTeamAsync(
                new UpdateManagedTeam(team.Id, "Platform Reliability", false, team.Version, []),
                user.Id);
            updateTeamResult.Succeeded.Should().BeTrue();
            var listedTeams = await teamAdministration.ListTeamsAsync();
            listedTeams.Should().ContainSingle(candidate =>
                candidate.Id == team.Id
                && candidate.Name == "Platform Reliability"
                && candidate.Members.Count == 0);
            var staleTeamResult = await teamAdministration.UpdateTeamAsync(
                new UpdateManagedTeam(team.Id, "Stale update", false, team.Version, []),
                user.Id);
            staleTeamResult.Succeeded.Should().BeFalse();

            (await database.OwnerSubjects.CountAsync(subject => subject.TeamId == team.Id)).Should().Be(1);
            (await assignmentAccess.IsAssignedAsync(
                roleOnlyUser.Id,
                teamOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeFalse();
            var closedMembership = await database.TeamMembers.SingleAsync(member => member.TeamId == team.Id);
            closedMembership.EffectiveUntil.Should().NotBeNull();
            await VerifyMembershipPeriodsCannotOverlapAsync(
                connectionString,
                closedMembership,
                user.Id);

            var auditTrail = scope.ServiceProvider.GetRequiredService<IAuditTrailReader>();
            var teamAudit = await auditTrail.SearchAsync(new AuditSearchQuery(
                ActorUserId: user.Id,
                Action: "team.updated",
                Entity: team.Id.ToString()));
            teamAudit.TotalCount.Should().Be(1);
            teamAudit.Events[0].BeforeValues.Should().ContainKey("memberUserIds");
            teamAudit.Events[0].AfterValues.Should().ContainKey("memberUserIds");
            var boundedAuditPage = await auditTrail.SearchAsync(new AuditSearchQuery(
                ToDate: DateOnly.MaxValue,
                Page: int.MaxValue));
            boundedAuditPage.Page.Should().Be(1);
            boundedAuditPage.Events.Should().NotBeEmpty();
            var userAudit = await auditTrail.SearchAsync(new AuditSearchQuery(
                ActorUserId: user.Id,
                Action: "user.updated",
                Entity: managedUser.Id.ToString()));
            userAudit.TotalCount.Should().Be(1);
            userAudit.Events[0].AfterValues["passwordReset"].Should().Be("true");
            userAudit.Events[0].AfterValues.Values.Should().NotContain(replacementPassword);
            (await database.OwnerSubjects.AnyAsync(subject => subject.UserId == roleOnlyUser.Id))
                .Should().BeTrue();
            var userOwnerSubjectId = await database.OwnerSubjects
                .Where(subject => subject.UserId == roleOnlyUser.Id)
                .Select(subject => subject.Id)
                .SingleAsync();
            (await assignmentAccess.IsAssignedAsync(
                roleOnlyUser.Id,
                userOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeTrue();
            var disabledUserOwnerSubjectId = await database.OwnerSubjects
                .Where(subject => subject.UserId == managedUser.Id)
                .Select(subject => subject.Id)
                .SingleAsync();
            (await assignmentAccess.IsAssignedAsync(
                managedUser.Id,
                disabledUserOwnerSubjectId,
                DateTimeOffset.UtcNow)).Should().BeFalse();

            await VerifyDisabledMembershipRetentionAsync(scope.ServiceProvider, user);
            await VerifyConcurrentDisableIsObservedAsync(services, user, connectionString);

            var auditWriter = scope.ServiceProvider.GetRequiredService<IAuthorizationDenialAuditWriter>();
            await auditWriter.WriteAsync(new AuthorizationDenialAuditEntry(
                user.Id,
                DateTimeOffset.UtcNow,
                "GET",
                "/Administration/Users",
                "database-foundation-correlation"));
            var auditEvent = await database
                .AuditEvents
                .SingleAsync(candidate => candidate.Action == "authorization.denied");
            auditEvent.ActorUserId.Should().Be(user.Id);
            auditEvent.Action.Should().Be("authorization.denied");
            auditEvent.EntityIdentifier.Should().Be("/Administration/Users");
            auditEvent.Outcome.Should().Be("forbidden");

            await VerifyAuditRowsAreAppendOnlyAsync(connectionString, auditEvent.Id);
        }
    }

    private static async Task VerifyDisabledMembershipRetentionAsync(
        IServiceProvider services,
        ApplicationUser administrator)
    {
        var userAdministration = services.GetRequiredService<IUserAdministrationService>();
        var teamAdministration = services.GetRequiredService<ITeamAdministrationService>();
        var database = services.GetRequiredService<ApplicationDbContext>();
        var password = $"Retained-5!{Guid.NewGuid():N}";
        var createUser = await userAdministration.CreateUserAsync(
            new CreateManagedUser(
                "Retained Disabled Member",
                "retained-disabled@example.test",
                password,
                [ApplicationRoles.Viewer]),
            administrator.Id);
        createUser.Succeeded.Should().BeTrue();
        var memberId = createUser.UserId!.Value;
        var createTeam = await teamAdministration.CreateTeamAsync(
            new CreateManagedTeam("Retention Team", [memberId]),
            administrator.Id);
        createTeam.Succeeded.Should().BeTrue();

        var disableUser = await userAdministration.UpdateUserAsync(
            new UpdateManagedUser(
                memberId,
                "Retained Disabled Member",
                true,
                [ApplicationRoles.Viewer]),
            administrator.Id);
        disableUser.Succeeded.Should().BeTrue();
        var team = await teamAdministration.FindTeamAsync(createTeam.TeamId!.Value);
        var renameTeam = await teamAdministration.UpdateTeamAsync(
            new UpdateManagedTeam(
                team!.Id,
                "Renamed Retention Team",
                false,
                team.Version,
                [memberId]),
            administrator.Id);
        renameTeam.Succeeded.Should().BeTrue();
        (await database.TeamMembers.SingleAsync(member => member.TeamId == team.Id))
            .EffectiveUntil.Should().BeNull();

        var addDisabledUser = await teamAdministration.CreateTeamAsync(
            new CreateManagedTeam("Rejected Disabled Assignment", [memberId]),
            administrator.Id);
        addDisabledUser.Succeeded.Should().BeFalse();
    }

    private static async Task VerifyConcurrentDisableIsObservedAsync(
        IServiceProvider rootServices,
        ApplicationUser administrator,
        string connectionString)
    {
        Guid createUserId;
        Guid updateUserId;
        await using (var setupScope = rootServices.CreateAsyncScope())
        {
            var userAdministration = setupScope.ServiceProvider
                .GetRequiredService<IUserAdministrationService>();
            var createUser = await userAdministration.CreateUserAsync(
                new CreateManagedUser(
                    "Concurrent Create User",
                    "concurrent-create@example.test",
                    $"Concurrent-4!{Guid.NewGuid():N}",
                    [ApplicationRoles.Viewer]),
                administrator.Id);
            createUser.Succeeded.Should().BeTrue();
            createUserId = createUser.UserId!.Value;
            var updateUser = await userAdministration.CreateUserAsync(
                new CreateManagedUser(
                    "Concurrent Update User",
                    "concurrent-update@example.test",
                    $"Concurrent-3!{Guid.NewGuid():N}",
                    [ApplicationRoles.Viewer]),
                administrator.Id);
            updateUser.Succeeded.Should().BeTrue();
            updateUserId = updateUser.UserId!.Value;
        }

        await using var assignmentScope = rootServices.CreateAsyncScope();
        var teamAdministration = assignmentScope.ServiceProvider
            .GetRequiredService<ITeamAdministrationService>();
        var createResult = await RunDuringConcurrentDisableAsync(
            connectionString,
            createUserId,
            () => teamAdministration.CreateTeamAsync(
                new CreateManagedTeam("Concurrent Create Team", [createUserId]),
                administrator.Id));
        createResult.Succeeded.Should().BeFalse();

        var emptyTeamResult = await teamAdministration.CreateTeamAsync(
            new CreateManagedTeam("Concurrent Update Team", []),
            administrator.Id);
        emptyTeamResult.Succeeded.Should().BeTrue();
        var emptyTeam = await teamAdministration.FindTeamAsync(emptyTeamResult.TeamId!.Value);
        var updateResult = await RunDuringConcurrentDisableAsync(
            connectionString,
            updateUserId,
            () => teamAdministration.UpdateTeamAsync(
                new UpdateManagedTeam(
                    emptyTeam!.Id,
                    emptyTeam.Name,
                    false,
                    emptyTeam.Version,
                    [updateUserId]),
                administrator.Id));
        updateResult.Succeeded.Should().BeFalse();
    }

    private static async Task<TeamAdministrationResult> RunDuringConcurrentDisableAsync(
        string connectionString,
        Guid userId,
        Func<Task<TeamAdministrationResult>> mutation)
    {
        await using var blockingConnection = new NpgsqlConnection(connectionString);
        await blockingConnection.OpenAsync();
        await using var blockingTransaction = await blockingConnection.BeginTransactionAsync();
        await using (var disableCommand = new NpgsqlCommand(
                         "UPDATE web_health.app_user SET is_disabled = TRUE WHERE id = @id",
                         blockingConnection,
                         blockingTransaction))
        {
            disableCommand.Parameters.AddWithValue("id", userId);
            (await disableCommand.ExecuteNonQueryAsync()).Should().Be(1);
        }

        var mutationTask = mutation();
        await Task.Delay(200);
        mutationTask.IsCompleted.Should().BeFalse();

        await blockingTransaction.CommitAsync();
        return await mutationTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task VerifyAuditRowsAreAppendOnlyAsync(
        string connectionString,
        Guid auditEventId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE web_health.audit_event SET outcome = 'changed' WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", auditEventId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.RaiseException);
    }

    private static async Task VerifyMembershipPeriodsCannotOverlapAsync(
        string connectionString,
        TeamMember existingMembership,
        Guid actorUserId)
    {
        const string sql = """
            INSERT INTO web_health.team_member
                (id, team_id, user_id, effective_from, effective_until, created_at, created_by_user_id)
            VALUES
                (@id, @team_id, @user_id, @effective_from, @effective_until, @created_at, @actor_user_id);
            """;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("team_id", existingMembership.TeamId);
        command.Parameters.AddWithValue("user_id", existingMembership.UserId);
        command.Parameters.AddWithValue("effective_from", existingMembership.EffectiveFrom);
        command.Parameters.AddWithValue("effective_until", existingMembership.EffectiveUntil!.Value);
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
    }

    private static async Task<(bool SchemaExists, IReadOnlyList<string> Tables)> ReadFoundationState(
        string connectionString)
    {
        const string sql = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'web_health'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return (tables.Count > 0, tables);
    }
}
