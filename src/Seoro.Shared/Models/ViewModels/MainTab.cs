namespace Seoro.Shared.Models.ViewModels;

public enum MainTabType
{
    Chat,
    FileDiff,
    FileContent,
    GitGraph
}

public class MainTab
{
    public bool ContentEvicted { get; set; }
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
    public FileDiff? DiffData { get; set; }
    public long ContentSizeBytes { get; set; }
    public MainTabType Type { get; init; }
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public string? DisambiguatedTitle { get; set; }
    public string? FileContent { get; set; }
    public string? FilePath { get; init; }

    /// <summary>
    ///     FileDiff 탭이 커밋 단위 diff을 보여줄 때의 커밋 SHA. working-tree 변경이면 null.
    ///     같은 파일을 워킹트리 변경과 커밋 변경으로 동시에 열 수 있도록 dedupe 키에 포함된다.
    /// </summary>
    public string? CommitSha { get; init; }

    // --- 편집 추적 (FileContent 탭 전용) ---
    public string? OriginalContent { get; set; }
    public bool IsDirty { get; set; }
    public DateTime? LastSavedAt { get; set; }
    public DateTime? LastLoadedMtimeUtc { get; set; }
}