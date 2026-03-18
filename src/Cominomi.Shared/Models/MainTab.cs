namespace Cominomi.Shared.Models;

public enum MainTabType { Chat, FileDiff, FileContent, Activity }

public class MainTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public MainTabType Type { get; set; }
    public string Title { get; set; } = "";
    public string? FilePath { get; set; }
    public FileDiff? DiffData { get; set; }
    public string? FileContent { get; set; }
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
    public bool ContentEvicted { get; set; }
    public long ContentSizeBytes { get; set; }
}
