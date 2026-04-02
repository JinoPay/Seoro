namespace Cominomi.Shared.Models;

public class GitRepoInfo
{
    public DateTime ClonedAt { get; set; } = DateTime.UtcNow;
    public string DefaultBranch { get; set; } = "main";
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string LocalPath { get; set; } = "";
    public string RemoteUrl { get; set; } = "";
}