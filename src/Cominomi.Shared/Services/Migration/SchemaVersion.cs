using System.Text.Json.Nodes;

namespace Cominomi.Shared.Services.Migration;

/// <summary>
///     Helpers for reading and writing the $schemaVersion field in JSON documents.
/// </summary>
public static class SchemaVersion
{
    public const string FieldName = "$schemaVersion";

    public static int Read(JsonObject doc)
    {
        if (doc.TryGetPropertyValue(FieldName, out var node) && node is JsonValue val &&
            val.TryGetValue<int>(out var v))
            return v;
        return 1; // Files without version field are treated as v1
    }

    public static void Write(JsonObject doc, int version)
    {
        doc[FieldName] = version;
    }
}