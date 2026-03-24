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

public enum DiffLineType { Context, Addition, Deletion, Hunk, Meta }

public class DiffLine
{
    public DiffLineType Type { get; set; }
    public string Prefix { get; set; } = "";
    public string Text { get; set; } = "";
    public string RawLine { get; set; } = "";
}

public class DiffHunk
{
    public int OldStart { get; set; }
    public int OldCount { get; set; }
    public int NewStart { get; set; }
    public int NewCount { get; set; }
    public string HeaderText { get; set; } = "";
    public int GapStartLine { get; set; }
    public int GapEndLine { get; set; }
    public bool IsExpanded { get; set; }
    public List<DiffLine>? ExpandedLines { get; set; }
    public List<DiffLine> Lines { get; set; } = [];
}

public class ParsedDiff
{
    public List<DiffLine> MetaLines { get; set; } = [];
    public List<DiffHunk> Hunks { get; set; } = [];
}
