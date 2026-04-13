namespace Seoro.Shared.Services.Git;

/// <summary>
///     워크스페이스의 원격 호스트 종류.
///     1단계에서는 GitHub만 정식 지원한다. GitLab/Bitbucket 등은 <see cref="Other"/>로 분류해
///     푸시·머지 UI를 전부 숨긴다 (폐기 결정).
/// </summary>
public enum RemoteMode
{
    /// <summary>원격이 없거나 감지 실패. 로컬 머지만 가능.</summary>
    None,

    /// <summary>github.com 호스트로 확인됨. 푸시 + compare URL 워크플로 활성화.</summary>
    GitHub,

    /// <summary>GitHub 이외의 호스트 (GitLab/Bitbucket 등). 1단계 미지원.</summary>
    Other
}

/// <summary>
///     워크스페이스의 <c>origin</c> 원격 정보를 파싱한 결과.
///     <see cref="Services.Infrastructure.WorkspaceService"/>가 로드 시점에
///     <c>GetRemoteUrlAsync</c> + <see cref="GitHubUrlHelper.BuildRemoteInfo(string?)"/>로
///     인메모리 캐시에 채워 넣는다. 디스크 영속화하지 않음 — PR #245 교훈에 따라
///     자동 감지 결과를 세션/워크스페이스 JSON에 저장하지 않는다.
/// </summary>
public sealed record RemoteInfo(
    RemoteMode Mode,
    string? Url,
    string? Owner,
    string? Repo)
{
    /// <summary>
    ///     감지 실패 또는 원격이 없을 때 사용하는 기본값.
    ///     <c>null</c> 대신 이 인스턴스를 돌려주면 소비자 분기가 단순해진다.
    /// </summary>
    public static RemoteInfo None { get; } = new(RemoteMode.None, null, null, null);
}
