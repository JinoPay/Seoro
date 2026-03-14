namespace Cominomi.Shared.Models;

public class AppSettings
{
    public string DefaultModel { get; set; } = "claude-sonnet-4-20250514";
    public string Theme { get; set; } = "dark";
    public string DefaultWorkingDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
