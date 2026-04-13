using System.Text.Json.Nodes;

namespace Seoro.Shared.Services.Migration;

/// <summary>
///     Session v4 → v5: git.lastPrUrl 을 git.trackedPr 객체로 승격한다.
/// </summary>
public class SessionV4ToV5Migration : IJsonMigration
{
    public int FromVersion => 4;
    public int ToVersion => 5;

    public JsonObject Migrate(JsonObject document)
    {
        if (document["git"] is not JsonObject git)
            return document;

        if (!git.ContainsKey("trackedPr"))
        {
            var trackedPr = new JsonObject();
            if (git["lastPrUrl"] is JsonValue lastPrUrl)
                trackedPr["url"] = lastPrUrl.GetValue<string?>();
            git["trackedPr"] = trackedPr;
        }

        return document;
    }
}
