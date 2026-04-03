namespace Cominomi.Shared.Models;

public class ClaudeAccountStore
{
    public int SchemaVersion { get; set; } = 1;
    public string? ActiveAccountId { get; set; }
    public List<ClaudeAccount> Accounts { get; set; } = [];
}
