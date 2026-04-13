using System.Collections.Concurrent;

namespace Seoro.Shared.Services.Migration;

/// <summary>
///     Central registry of SchemaMigrators for each persisted model type.
///     Initialized once at startup; provides fast lookup by type.
/// </summary>
public static class SchemaMigratorRegistry
{
    private static readonly ConcurrentDictionary<Type, SchemaMigrator> Migrators = new();

    static SchemaMigratorRegistry()
    {
        // Session: v1 had flat format (worktreePath, branchName at root)
        // v2 introduced nested git/pr objects — migration handled by SessionJsonConverter
        // v3 removed PrContext and PR-related status values (Pushed, PrOpen, ConflictDetected, Merged)
        // v4 reintroduced a single manual-only git.lastPrUrl (user-pasted, never auto-populated)
        // v5 upgrades it into a trackedPr object for PR status/merge automation.
        Register<Session>(new SchemaMigrator(5, [new SessionV3ToV4Migration(), new SessionV4ToV5Migration()]));

        // All other models start at v1 with no migrations yet.
        // When a schema change is needed, bump the version and add migrations.
        Register<Workspace>(new SchemaMigrator(2, [new WorkspaceV1ToV2Migration()]));
        Register<AppSettings>(new SchemaMigrator(1));
        Register<MemoryEntry>(new SchemaMigrator(1));
        Register<TaskItem>(new SchemaMigrator(1));
        Register<GitRepoInfo>(new SchemaMigrator(1));
        Register<HookDefinition>(new SchemaMigrator(1));
    }

    public static SchemaMigrator? GetMigrator<T>() where T : class
    {
        return Migrators.GetValueOrDefault(typeof(T));
    }

    public static SchemaMigrator? GetMigrator(Type type)
    {
        return Migrators.GetValueOrDefault(type);
    }

    public static void Register<T>(SchemaMigrator migrator) where T : class
    {
        Migrators[typeof(T)] = migrator;
    }
}
