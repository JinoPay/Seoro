namespace Cominomi.Shared.Models;

public class GitRepoInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RemoteUrl { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string DefaultBranch { get; set; } = "main";
    public DateTime ClonedAt { get; set; } = DateTime.UtcNow;
}
