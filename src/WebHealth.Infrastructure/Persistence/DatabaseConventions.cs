using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WebHealth.Infrastructure.Persistence;

internal static partial class DatabaseConventions
{
    public const string DefaultSchema = "web_health";
    public const string MigrationsHistoryTable = "__ef_migrations_history";
    public const string TimestampWithTimeZone = "timestamp with time zone";

    public static void Configure(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveColumnType(TimestampWithTimeZone);
    }

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DefaultSchema);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            ApplyEntityNames(entityType);
            ApplyRestrictiveDeletes(entityType);
        }
    }

    public static string ToSnakeCase(string name)
    {
        var wordsSeparated = AcronymBoundary().Replace(name, "$1_$2");
        return WordBoundary().Replace(wordsSeparated, "$1_$2").ToLowerInvariant();
    }

    private static void ApplyEntityNames(IMutableEntityType entityType)
    {
        entityType.SetTableName(ToSnakeCase(entityType.GetTableName()!));
        ApplyPropertyNames(entityType);
        ApplyKeyNames(entityType);
        ApplyForeignKeyNames(entityType);
        ApplyIndexNames(entityType);
    }

    private static void ApplyPropertyNames(IMutableEntityType entityType)
    {
        foreach (var property in entityType.GetProperties())
        {
            property.SetColumnName(ToSnakeCase(property.GetColumnName()));
        }
    }

    private static void ApplyKeyNames(IMutableEntityType entityType)
    {
        foreach (var key in entityType.GetKeys())
        {
            key.SetName(ToSnakeCase(key.GetName()!));
        }
    }

    private static void ApplyForeignKeyNames(IMutableEntityType entityType)
    {
        foreach (var foreignKey in entityType.GetForeignKeys())
        {
            foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));
        }
    }

    private static void ApplyIndexNames(IMutableEntityType entityType)
    {
        foreach (var index in entityType.GetIndexes())
        {
            index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
        }
    }

    private static void ApplyRestrictiveDeletes(IMutableEntityType entityType)
    {
        foreach (var foreignKey in entityType.GetForeignKeys())
        {
            var conventionForeignKey = (IConventionForeignKey)foreignKey;
            if (conventionForeignKey.GetDeleteBehaviorConfigurationSource() == ConfigurationSource.Explicit)
            {
                continue;
            }

            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    [GeneratedRegex("([A-Z]+)([A-Z][a-z])")]
    private static partial Regex AcronymBoundary();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex WordBoundary();
}
