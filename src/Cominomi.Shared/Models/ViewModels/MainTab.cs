namespace Cominomi.Shared.Models.ViewModels;

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
}