using System.Text.Json;

namespace Cominomi.Shared.Services.Migration;

/// <summary>
/// Serializes objects to JSON with $schemaVersion stamped automatically.
/// </summary>
public static class MigratingJsonWriter
{
    /// <summary>
    /// Serialize an object to JSON with $schemaVersion injected.
    /// </summary>
    public static string Write<T>(T obj, JsonSerializerOptions options) where T : class
    {
        var migrator = SchemaMigratorRegistry.GetMigrator<T>();
        if (migrator == null)
            return JsonSerializer.Serialize(obj, options);

        return migrator.SerializeWithVersion(obj, options);
    }
}
