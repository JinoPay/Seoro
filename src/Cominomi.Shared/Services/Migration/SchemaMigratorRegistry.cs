using System.Collections.Concurrent;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services.Migration;

/// <summary>
/// Central registry of SchemaMigrators for each persisted model type.
/// Initialized once at startup; provides fast lookup by type.
/// </summary>
public static class SchemaMigratorRegistry
{
    private static readonly ConcurrentDictionary<Type, SchemaMigrator> Migrators = new();

    static SchemaMigratorRegistry()
    {
        // Session: v1 had flat format (worktreePath, branchName at root)
        // v2 introduced nested git/pr objects — migration handled by SessionJsonConverter
        // We register v2 as current but no IJsonMigration needed since SessionJsonConverter
        // already handles v1→v2 in its Read() method.
        Register<Session>(new SchemaMigrator(2));

        // All other models start at v1 with no migrations yet.
        // When a schema change is needed, bump the version and add migrations.
        Register<Workspace>(new SchemaMigrator(1));
        Register<AppSettings>(new SchemaMigrator(1));
        Register<MemoryEntry>(new SchemaMigrator(1));
        Register<TaskItem>(new SchemaMigrator(1));
        Register<GitRepoInfo>(new SchemaMigrator(1));
        Register<HookDefinition>(new SchemaMigrator(1));
    }

    public static void Register<T>(SchemaMigrator migrator) where T : class
    {
        Migrators[typeof(T)] = migrator;
    }

    public static SchemaMigrator? GetMigrator<T>() where T : class
    {
        return Migrators.GetValueOrDefault(typeof(T));
    }

    public static SchemaMigrator? GetMigrator(Type type)
    {
        return Migrators.GetValueOrDefault(type);
    }
}
