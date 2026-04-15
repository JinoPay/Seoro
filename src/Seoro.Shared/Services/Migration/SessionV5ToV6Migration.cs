using System.Text.Json.Nodes;

namespace Seoro.Shared.Services.Migration;

/// <summary>
///     v5 → v6: disabledMcpServers 필드 추가. 기존 세션에 없으면 빈 배열 기본값 사용.
///     역직렬화 시 기본값이 적용되므로 실제 변환 로직은 없음.
/// </summary>
public class SessionV5ToV6Migration : IJsonMigration
{
    public int FromVersion => 5;
    public int ToVersion => 6;

    public JsonObject Migrate(JsonObject document) => document;
}
