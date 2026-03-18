namespace Cominomi.Shared.Models;

public enum MainTabType { Chat, FileDiff, FileContent, Activity }

public class MainTab
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public MainTabType Type { get; init; }
    public string Title { get; set; } = "";
    public string? FilePath { get; init; }
    public FileDiff? DiffData { get; set; }
    public string? FileContent { get; set; }
}
