namespace Seoro.Shared.Services.Codex.AppServer;

/// <summary>
///     Codex app-server 기동 인자 빌더.
///     app-server는 모델/승인/샌드박스를 CLI 플래그가 아니라 thread/start·turn/start params로 받으므로
///     기동 인자는 서브커맨드만으로 충분하다(권한 정책은 codex 기본 설정 또는 추후 thread params로).
/// </summary>
internal static class CodexAppServerArgumentBuilder
{
    public static string Build(string baseArgs) => $"{baseArgs}app-server";
}
