namespace Cominomi.Shared.Models;

public enum MainTabType { Chat, FileDiff, FileContent }

public class MainTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public MainTabType Type { get; set; }
    public string Title { get; set; } = "";
    public string? FilePath { get; set; }
    public FileDiff? DiffData { get; set; }
    public string? FileContent { get; set; }
}
