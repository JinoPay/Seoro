namespace Cominomi.Shared.Models;

public class ActivityEntry
{
    public string CommitHash { get; init; } = "";
    public string ShortHash { get; init; } = "";
    public string Message { get; init; } = "";
    public string Author { get; init; } = "";
    public DateTime Timestamp { get; init; }
    public string SessionId { get; init; } = "";
    public string SessionTitle { get; init; } = "";
    public string BranchName { get; init; } = "";
    public SessionStatus SessionStatus { get; init; }
}

public class ActivityDateGroup
{
    public string Label { get; init; } = "";
    public List<ActivityEntry> Entries { get; init; } = [];
}
