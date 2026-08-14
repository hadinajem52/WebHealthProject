using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebHealth.Application.Registry;
using WebHealth.Domain.Normalization;
using WebHealth.Infrastructure.Persistence;

namespace WebHealth.Infrastructure.Registry;

internal sealed class RegistryMutationSupport(ApplicationDbContext dbContext)
{
    public static List<string> ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ["Enter a name."];
        }

        return name.Length > 200 ? ["The name cannot exceed 200 characters."] : [];
    }

    public static string NormalizeOptionalText(string? value) => value?.Trim() ?? string.Empty;

    public async Task<bool> LockValidOwnerAsync(
        Guid ownerSubjectId,
        Guid? retainedOwnerSubjectId,
        CancellationToken cancellationToken)
    {
        var owner = await dbContext.OwnerSubjects
            .FromSqlInterpolated($"""
                SELECT * FROM web_health.owner_subject
                WHERE id = {ownerSubjectId}
                FOR SHARE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (owner is null)
        {
            return false;
        }

        var isRetained = owner.Id == retainedOwnerSubjectId;
        if (owner.UserId is { } userId)
        {
            var user = await dbContext.Users
                .FromSqlInterpolated($"""
                    SELECT * FROM web_health.app_user
                    WHERE id = {userId}
                    FOR SHARE
                    """)
                .AsNoTracking()
                .SingleAsync(cancellationToken);
            return !user.IsDisabled || isRetained;
        }

        var team = await dbContext.Teams
            .FromSqlInterpolated($"""
                SELECT * FROM web_health.team
                WHERE id = {owner.TeamId!.Value}
                FOR SHARE
                """)
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        return !team.IsDisabled || isRetained;
    }

    public static bool IsConstraintViolation(DbUpdateException exception, string constraintName) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: var actualConstraint
        }
        && actualConstraint == constraintName;

    public static string NormalizeName(string name) => NameNormalizer.Normalize(name);

    public static string TrimName(string name) => NameNormalizer.TrimDisplayName(name);
}
