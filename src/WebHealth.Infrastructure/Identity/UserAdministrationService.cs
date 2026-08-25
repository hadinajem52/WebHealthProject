using WebHealth.Application;
using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebHealth.Application.Administration;
using WebHealth.Application.Auditing;
using WebHealth.Infrastructure.Persistence;
using WebHealth.Infrastructure.Assignments;

namespace WebHealth.Infrastructure.Identity;

public sealed class UserAdministrationService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IAuditTrailWriter auditTrail,
    ILogger<UserAdministrationService> logger) : IUserAdministrationService
{
    private static readonly HashSet<string> SupportedRoles =
        ApplicationRoles.All.Select(role => role.Name).ToHashSet(StringComparer.Ordinal);

    public async Task<IReadOnlyList<ManagedUser>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await userManager.Users
            .AsNoTracking()
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Email)
            .ToListAsync(cancellationToken);
        var result = new List<ManagedUser>(users.Count);

        foreach (var user in users)
        {
            result.Add(ToManagedUser(user, await userManager.GetRolesAsync(user)));
        }

        return result;
    }

    public async Task<ManagedUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
        return user is null ? null : ToManagedUser(user, await userManager.GetRolesAsync(user));
    }

    public async Task<UserAdministrationResult> CreateUserAsync(
        CreateManagedUser command,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var roles = NormalizeRoles(command.Roles);
        var roleErrors = ValidateRoles(roles);
        if (roleErrors.Count > 0)
        {
            return UserAdministrationResult.Failure(roleErrors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = command.Email.Trim(),
            Email = command.Email.Trim(),
            DisplayName = command.DisplayName.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        var createResult = await userManager.CreateAsync(user, command.Password);
        if (!createResult.Succeeded)
        {
            return IdentityFailure(createResult);
        }

        var roleResult = await userManager.AddToRolesAsync(user, roles);
        if (!roleResult.Succeeded)
        {
            return IdentityFailure(roleResult);
        }

        dbContext.OwnerSubjects.Add(new OwnerSubject
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CreatedAt = now,
            CreatedByUserId = actorUserId
        });
        await auditTrail.RecordUserCreatedAsync(
            new AuditWriteContext(actorUserId, now),
            ToAuditSnapshot(user, roles, passwordReset: false),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Administrator {ActorUserId} created user {TargetUserId} with roles {Roles}",
            actorUserId,
            user.Id,
            roles);
        return UserAdministrationResult.Success(user.Id);
    }

    public async Task<UserAdministrationResult> UpdateUserAsync(
        UpdateManagedUser command,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var roles = NormalizeRoles(command.Roles);
        var roleErrors = ValidateRoles(roles);
        if (roleErrors.Count > 0)
        {
            return UserAdministrationResult.Failure(roleErrors);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
        {
            return UserAdministrationResult.Failure(
                "This account no longer exists. Return to Users to see the current list.");
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var beforeValues = ToAuditSnapshot(user, currentRoles, passwordReset: false);
        var removesAdministrator = currentRoles.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal)
            && !roles.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal);

        if (user.Id == actorUserId && (command.IsDisabled || removesAdministrator))
        {
            return UserAdministrationResult.Failure(
                "You cannot disable your own account or remove your own Administrator role. "
                + "Ask another Administrator to make this change.");
        }

        if ((command.IsDisabled || removesAdministrator)
            && currentRoles.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal)
            && await IsLastEnabledAdministratorAsync(user.Id, cancellationToken))
        {
            return UserAdministrationResult.Failure(
                "This is the last enabled Administrator, and disabling or demoting it would leave "
                + "nobody able to administer the system. Give another account the Administrator "
                + "role first.");
        }

        var disabledStateChanged = user.IsDisabled != command.IsDisabled;
        user.DisplayName = command.DisplayName.Trim();
        user.IsDisabled = command.IsDisabled;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return IdentityFailure(updateResult);
        }

        var rolesToRemove = currentRoles.Except(roles, StringComparer.Ordinal).ToArray();
        var rolesToAdd = roles.Except(currentRoles, StringComparer.Ordinal).ToArray();
        var rolesChanged = rolesToRemove.Length > 0 || rolesToAdd.Length > 0;
        var removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
        if (!removeResult.Succeeded)
        {
            return IdentityFailure(removeResult);
        }

        var addResult = await userManager.AddToRolesAsync(user, rolesToAdd);
        if (!addResult.Succeeded)
        {
            return IdentityFailure(addResult);
        }

        if (disabledStateChanged || rolesChanged)
        {
            var stampResult = await userManager.UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded)
            {
                return IdentityFailure(stampResult);
            }
        }

        if (!string.IsNullOrWhiteSpace(command.NewPassword))
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            var passwordResult = await userManager.ResetPasswordAsync(user, resetToken, command.NewPassword);
            if (!passwordResult.Succeeded)
            {
                return IdentityFailure(passwordResult);
            }
        }

        await auditTrail.RecordUserUpdatedAsync(
            new AuditWriteContext(actorUserId, DateTimeOffset.UtcNow),
            beforeValues,
            ToAuditSnapshot(user, roles, !string.IsNullOrWhiteSpace(command.NewPassword)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Administrator {ActorUserId} updated user {TargetUserId}; disabled is {IsDisabled}; roles are {Roles}; password reset is {PasswordReset}",
            actorUserId,
            user.Id,
            user.IsDisabled,
            roles,
            !string.IsNullOrWhiteSpace(command.NewPassword));
        return UserAdministrationResult.Success(user.Id);
    }

    private async Task<bool> IsLastEnabledAdministratorAsync(Guid excludedUserId, CancellationToken cancellationToken)
    {
        var administratorRoleId = ApplicationRoles.All
            .Single(role => role.Name == ApplicationRoles.Administrator)
            .Id;

        return !await dbContext.Users
            .Where(user => user.Id != excludedUserId && !user.IsDisabled)
            .Join(
                dbContext.UserRoles.Where(userRole => userRole.RoleId == administratorRoleId),
                user => user.Id,
                userRole => userRole.UserId,
                (user, _) => user.Id)
            .AnyAsync(cancellationToken);
    }

    private static ManagedUser ToManagedUser(ApplicationUser user, IEnumerable<string> roles) =>
        new(
            user.Id,
            user.DisplayName,
            user.Email ?? string.Empty,
            user.IsDisabled,
            roles.OrderBy(role => role, StringComparer.Ordinal).ToArray());

    private static string[] NormalizeRoles(IEnumerable<string> roles) =>
        roles.Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();

    private static List<ValidationError> ValidateRoles(IReadOnlyCollection<string> roles)
    {
        var errors = new List<ValidationError>();
        if (roles.Count == 0)
        {
            errors.Add(ValidationError.For("Roles", "Select at least one role."));
        }

        var unsupported = roles.Where(role => !SupportedRoles.Contains(role)).ToArray();
        if (unsupported.Length > 0)
        {
            errors.Add(ValidationError.For(
                "Roles",
                $"These roles are not recognised: {string.Join(", ", unsupported)}. "
                + "Reload the page and choose from the list."));
        }

        return errors;
    }

    private static UserAdministrationResult IdentityFailure(IdentityResult result) =>
        UserAdministrationResult.Failure(result.Errors
            .Select(DescribeIdentityError)
            .Where(error => error is not null)
            .Select(error => error!)
            .DistinctBy(error => error.Message, StringComparer.Ordinal));

    private static ValidationError? DescribeIdentityError(IdentityError error) => error.Code switch
    {
        "DuplicateUserName" => ValidationError.For(
            "Email", "An account already uses this email address. Use a different one."),
        "InvalidUserName" => ValidationError.For(
            "Email", "Enter a valid email address, such as name@example.com."),
        "DuplicateEmail" => ValidationError.For(
            "Email", "An account already uses this email address. Use a different one."),
        "InvalidEmail" => ValidationError.For(
            "Email", "Enter a valid email address, such as name@example.com."),
        "PasswordTooShort" => ValidationError.For(
            "Password", "The password must be at least 12 characters."),
        "PasswordRequiresDigit" => ValidationError.For(
            "Password", "The password must include at least one digit (0-9)."),
        "PasswordRequiresLower" => ValidationError.For(
            "Password", "The password must include at least one lowercase letter."),
        "PasswordRequiresUpper" => ValidationError.For(
            "Password", "The password must include at least one uppercase letter."),
        "PasswordRequiresNonAlphanumeric" => ValidationError.For(
            "Password", "The password must include at least one symbol, such as ! ? # or %."),
        "PasswordRequiresUniqueChars" => ValidationError.For(
            "Password", "The password must use at least 4 different characters."),
        "PasswordMismatch" => ValidationError.For(
            "Password", "That password is not correct."),
        "UserAlreadyHasPassword" => ValidationError.For(
            "Password", "This account already has a password. Use the reset field instead."),
        "UserAlreadyInRole" or "UserNotInRole" =>
            "The roles on this account changed while you were editing it. Reload the page and try again.",
        "ConcurrencyFailure" =>
            "Someone else changed this account after you opened it, so your change was not applied. "
            + "Reload the page to see the current values, then reapply your change.",
        _ => "This account could not be saved. Reload the page and try again."
    };

    private static UserAuditSnapshot ToAuditSnapshot(
        ApplicationUser user,
        IEnumerable<string> roles,
        bool passwordReset) => new(
            user.Id,
            user.DisplayName,
            user.Email ?? string.Empty,
            user.IsDisabled,
            roles.OrderBy(role => role, StringComparer.Ordinal).ToArray(),
            passwordReset);
}
