namespace Seoro.Shared.Models.Knowledge;

public class ContextInfo
{
    public List<PlanFile> Plans { get; set; } = [];
    public string Notes { get; set; } = string.Empty;
    public string Todos { get; set; } = string.Empty;
}

public class PlanFile
{
    public DateTime LastModified { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}