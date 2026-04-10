using System.Text.Json.Nodes;

namespace Seoro.Shared.Services.Migration;

/// <summary>
///     Workspace v1 → v2: SortIndex 속성 추가 (워크스페이스 순서 지정용).
/// </summary>
public class WorkspaceV1ToV2Migration : IJsonMigration
{
    public int FromVersion => 1;
    public int ToVersion => 2;

    public JsonObject Migrate(JsonObject document)
    {
        document["sortIndex"] = int.MaxValue;
        return document;
    }
}
