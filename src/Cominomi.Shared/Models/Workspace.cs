namespace Cominomi.Shared.Models;

public class Workspace
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Default";
    public string DefaultWorkingDirectory { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = ModelDefinitions.Default.Id;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
