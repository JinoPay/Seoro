namespace Cominomi.Shared.Models;

public class AppSettings
{
    public string DefaultModel { get; set; } = ModelDefinitions.Default.Id;
    public string Theme { get; set; } = "dark";
    public string DefaultWorkingDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string? ClaudePath { get; set; }
    public List<string> RecentDirectories { get; set; } = [];
}
