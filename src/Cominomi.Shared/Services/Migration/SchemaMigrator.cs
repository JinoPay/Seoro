using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cominomi.Shared.Services.Migration;

/// <summary>
///     Applies sequential schema migrations to bring JSON from any past version to the current version.
/// </summary>
public class SchemaMigrator
{
    private readonly SortedList<int, IJsonMigration> _migrations = new();

    public SchemaMigrator(int currentVersion, IEnumerable<IJsonMigration>? migrations = null)
    {
        CurrentVersion = currentVersion;
        if (migrations != null)
            foreach (var m in migrations)
                _migrations.Add(m.FromVersion, m);
    }

    public int CurrentVersion { get; }

    /// <summary>
    ///     Migrates a JSON string to the current schema version.
    ///     Returns the deserialized object and whether any migration was applied.
    /// </summary>
    public MigrationResult<T> DeserializeAndMigrate<T>(string json, JsonSerializerOptions options) where T : class
    {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject doc)
            return new MigrationResult<T>(JsonSerializer.Deserialize<T>(json, options), false);

        var version = SchemaVersion.Read(doc);
        if (version >= CurrentVersion)
            // Already at current version — deserialize directly (faster path)
            return new MigrationResult<T>(JsonSerializer.Deserialize<T>(json, options), false);

        // Apply migrations sequentially
        while (version < CurrentVersion)
        {
            if (!_migrations.TryGetValue(version, out var migration))
                break; // No migration found for this version — stop

            doc = migration.Migrate(doc);
            version = migration.ToVersion;
        }

        // Stamp current version
        SchemaVersion.Write(doc, CurrentVersion);

        var migratedJson = doc.ToJsonString(options);
        var result = JsonSerializer.Deserialize<T>(migratedJson, options);
        return new MigrationResult<T>(result, true, migratedJson);
    }

    /// <summary>
    ///     Injects $schemaVersion into serialized JSON.
    /// </summary>
    public string SerializeWithVersion<T>(T obj, JsonSerializerOptions options)
    {
        var json = JsonSerializer.Serialize(obj, options);
        var node = JsonNode.Parse(json);
        if (node is JsonObject doc)
        {
            SchemaVersion.Write(doc, CurrentVersion);
            return doc.ToJsonString(options);
        }

        return json;
    }
}

public record MigrationResult<T>(T? Result, bool WasMigrated, string? MigratedJson = null);