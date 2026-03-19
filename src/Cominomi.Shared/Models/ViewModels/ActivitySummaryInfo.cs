namespace Cominomi.Shared.Models.ViewModels;

public class ActivitySummaryInfo
{
    public int TotalToolCalls { get; set; }
    public int TextSegments { get; set; }
    public int ThinkingBlocks { get; set; }
    public bool HasErrors { get; set; }
    public List<FileChangeInfo> FileChanges { get; set; } = [];

    public string SummaryText
    {
        get
        {
            var parts = new List<string>();
            if (TotalToolCalls > 0) parts.Add($"도구 {TotalToolCalls}회");
            if (TextSegments > 0) parts.Add($"메시지 {TextSegments}개");
            return string.Join(", ", parts);
        }
    }
}

public class FileChangeInfo
{
    public string FilePath { get; set; } = "";
    public string FileName => Path.GetFileName(FilePath);
    public string ToolAction { get; set; } = "";
}
