namespace Cominomi.Shared.Models;

public class ClaudeAccount
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string ProfileName { get; set; } = "";
    public string EmailAddress { get; set; } = "";
    public string AccountUuid { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? LastSwitchedAt { get; set; }
    public int SwitchCount { get; set; }
    public long TotalActiveSeconds { get; set; }
}
