namespace Cominomi.Shared.Models.ViewModels;

public enum MainTabType { Chat, FileDiff, FileContent, Activity, Notifications }

public class MainTab
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public MainTabType Type { get; init; }
    public string Title { get; set; } = "";
    public string? FilePath { get; init; }
    public FileDiff? DiffData { get; set; }
    public string? FileContent { get; set; }
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
    public bool ContentEvicted { get; set; }
    public long ContentSizeBytes { get; set; }
}
