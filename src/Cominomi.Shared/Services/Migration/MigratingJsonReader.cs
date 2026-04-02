using System.Text.Json;

namespace Cominomi.Shared.Services.Migration;

/// <summary>
///     Deserializes JSON with automatic schema migration. If the file is outdated,
///     migrations are applied and the caller receives the migrated JSON for write-back.
/// </summary>
public static class MigratingJsonReader
{
    /// <summary>
    ///     Deserialize JSON, applying any needed schema migrations.
    /// </summary>
    public static MigrationResult<T> Read<T>(string json, JsonSerializerOptions options) where T : class
    {
        var migrator = SchemaMigratorRegistry.GetMigrator<T>();
        if (migrator == null)
            // No migrator registered — plain deserialize
            return new MigrationResult<T>(JsonSerializer.Deserialize<T>(json, options), false);

        return migrator.DeserializeAndMigrate<T>(json, options);
    }
}