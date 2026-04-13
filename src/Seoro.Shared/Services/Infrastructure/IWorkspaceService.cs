
namespace Seoro.Shared.Services.Infrastructure;

public interface IWorkspaceService
{
    event Action<Workspace>? OnWorkspaceSaved;
    Task DeleteWorkspaceAsync(string workspaceId);
    Task SaveWorkspaceAsync(Workspace workspace);

    Task<GitRepoInfo?> FindExistingRepoAsync(string remoteUrl);

    Task<List<Workspace>> GetWorkspacesAsync();
    Task<string> GetWorktreesDirAsync();
    Task<Workspace?> LoadWorkspaceAsync(string workspaceId);
    Task<Workspace> CreateFromLocalAsync(string localPath, string name, string model, CancellationToken ct = default);

    Task<Workspace> CreateFromUrlAsync(string url, string name, string model, string? targetDir = null,
        IProgress<string>? progress = null, CancellationToken ct = default);

    Task<string> GetDefaultClonePathAsync(string url);

    /// <summary>
    ///     워크스페이스의 <c>origin</c> 원격 정보를 인메모리 캐시에서 조회한다.
    ///     캐시에 없으면 <see cref="RemoteInfo.None"/> 을 돌려준다 (PR #245 교훈대로
    ///     디스크에 영속화하지 않음 — 매 앱 실행마다 동적으로 감지).
    ///     캐시 초기화는 워크스페이스 로드/생성 경로에서 자동으로 일어난다.
    /// </summary>
    RemoteInfo GetRemoteInfo(string workspaceId);

    /// <summary>
    ///     워크스페이스의 원격 URL 을 강제로 다시 감지해 캐시를 갱신한다.
    ///     사용자가 외부에서 <c>git remote set-url</c> 을 변경한 경우 수동으로 호출해 반영.
    /// </summary>
    Task<RemoteInfo> RefreshRemoteInfoAsync(string workspaceId, CancellationToken ct = default);
}