namespace Seoro.Shared.Models.ViewModels;

public enum MainTabType
{
    Chat,
    FileDiff,
    FileContent
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

    // --- 편집 추적 (FileContent 탭 전용) ---
    public string? OriginalContent { get; set; }
    public bool IsDirty { get; set; }
    public DateTime? LastSavedAt { get; set; }
    public DateTime? LastLoadedMtimeUtc { get; set; }
}