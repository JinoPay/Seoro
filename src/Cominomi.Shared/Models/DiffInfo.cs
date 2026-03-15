using System.Text.Json.Serialization;

namespace Cominomi.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FileChangeType { Added, Modified, Deleted, Renamed }

public class FileDiff
{
    public string FilePath { get; set; } = "";
    public FileChangeType ChangeType { get; set; }
    public string UnifiedDiff { get; set; } = "";
    public int Additions { get; set; }
    public int Deletions { get; set; }
}

public class DiffSummary
{
    public List<FileDiff> Files { get; set; } = [];
    public int TotalAdditions => Files.Sum(f => f.Additions);
    public int TotalDeletions => Files.Sum(f => f.Deletions);
}
