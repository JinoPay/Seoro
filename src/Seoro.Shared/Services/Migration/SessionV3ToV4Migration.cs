using System.Text.Json.Nodes;

namespace Seoro.Shared.Services.Migration;

/// <summary>
///     Session v3 → v4: <c>git.lastPrUrl</c> 필드를 추가 (기본 null).
///     사용자가 push 후 GitHub에서 PR 을 만들면 UI 에서 수동으로 붙여넣어 저장한다.
///     ⚠ PR #245 교훈: 자동 감지·폴링 없이 오직 사용자 입력만으로 채운다.
/// </summary>
public class SessionV3ToV4Migration : IJsonMigration
{
    public int FromVersion => 3;
    public int ToVersion => 4;

    public JsonObject Migrate(JsonObject document)
    {
        // v3 에는 git 객체가 이미 있으므로 그 안에 lastPrUrl 키만 추가.
        // System.Text.Json 이 누락 키를 null 로 역직렬화하지만, 명시적으로 써서
        // "이 버전부터 필드가 도입됐다"는 스키마 흔적을 남긴다.
        if (document["git"] is JsonObject git && !git.ContainsKey("lastPrUrl"))
            git["lastPrUrl"] = null;
        return document;
    }
}
