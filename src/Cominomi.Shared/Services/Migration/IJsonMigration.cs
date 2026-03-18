using System.Text.Json.Nodes;

namespace Cominomi.Shared.Services.Migration;

/// <summary>
/// Defines a single schema migration step that transforms JSON from one version to the next.
/// </summary>
public interface IJsonMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    JsonObject Migrate(JsonObject document);
}
