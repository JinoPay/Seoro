namespace Seoro.Shared.Models.Common;

/// <summary>
///     워크트리 → 로컬 디렉터리 동기화 상태.
///     sync-state.json으로 디스크에 저장되어 크래시 복구 마커 역할도 수행합니다.
/// </summary>
public class SyncState
{
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public List<string> CopiedFromWorktree { get; set; } = [];
    public List<SyncBackupEntry> BackedUpFiles { get; set; } = [];
    public string BackupDir { get; set; } = "";
    public string BaseBranch { get; set; } = "";
    public string BaseCommit { get; set; } = "";
    public string RepoLocalPath { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string WorkspaceId { get; set; } = "";
    public string WorktreePath { get; set; } = "";
}

public class SyncBackupEntry
{
    public bool WasUntracked { get; set; }
    public string RelativePath { get; set; } = "";
}