using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebHealth.Infrastructure.Identity;

public sealed class AdminBootstrapper(
    RoleManager<ApplicationRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IOptions<BootstrapAdminOptions> options,
    ILogger<AdminBootstrapper> logger)
{
    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        Validate(settings);

        foreach (var definition in ApplicationRoles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var role = await roleManager.FindByNameAsync(definition.Name);
            if (role is null)
            {
                await EnsureSucceededAsync(roleManager.CreateAsync(new ApplicationRole
                {
                    Id = definition.Id,
                    Name = definition.Name,
                    Version = 1
                }));
                logger.LogInformation("Created application role {RoleName}.", definition.Name);
            }
            else if (role.Id != definition.Id)
            {
                throw new InvalidOperationException(
                    $"Role '{definition.Name}' exists with an unexpected identifier.");
            }
        }

        var user = await userManager.FindByEmailAsync(settings.Email);
        if (user is null)
        {
            var now = DateTimeOffset.UtcNow;
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = settings.Email,
                Email = settings.Email,
                EmailConfirmed = true,
                DisplayName = settings.DisplayName.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };

            await EnsureSucceededAsync(userManager.CreateAsync(user, settings.Password));
            logger.LogInformation("Created bootstrap administrator {UserId}.", user.Id);
        }

        if (user.IsDisabled)
        {
            throw new InvalidOperationException("The bootstrap administrator account is disabled.");
        }

        if (!await userManager.IsInRoleAsync(user, ApplicationRoles.Administrator))
        {
            await EnsureSucceededAsync(
                userManager.AddToRoleAsync(user, ApplicationRoles.Administrator));
            logger.LogInformation("Granted Administrator to bootstrap user {UserId}.", user.Id);
        }
    }

    private static void Validate(BootstrapAdminOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Email)
            || string.IsNullOrWhiteSpace(options.DisplayName)
            || string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException(
                "BootstrapAdmin:Email, DisplayName, and Password must be supplied through secret configuration.");
        }
    }

    private static async Task EnsureSucceededAsync(Task<IdentityResult> operation)
    {
        var result = await operation;
        if (!result.Succeeded)
        {
            var descriptions = string.Join("; ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Identity bootstrap failed: {descriptions}");
        }
    }
}
