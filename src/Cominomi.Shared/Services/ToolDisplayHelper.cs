using System.Text.Json;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public static class ToolDisplayHelper
{
    /// <summary>
    /// Returns a contextual header label like "Read MessageBubble.razor" or "Grep pattern".
    /// </summary>
    public static string GetHeaderLabel(ToolCall tool)
    {
        var name = NormalizeToolName(tool.Name);
        var detail = ExtractHeaderDetail(name, tool.Input);
        return detail != null ? $"{name} {detail}" : name;
    }

    /// <summary>
    /// Returns a compact result hint like "50줄 읽음" or "3개 파일 일치".
    /// Returns null if no meaningful hint is available.
    /// </summary>
    public static string? GetCompactResult(ToolCall tool)
    {
        if (!tool.IsComplete || string.IsNullOrEmpty(tool.Output))
            return null;

        var name = NormalizeToolName(tool.Name);
        return name switch
        {
            "Read" => GetReadHint(tool.Output),
            "Grep" => GetGrepHint(tool.Output),
            "Glob" => GetGlobHint(tool.Output),
            _ => null
        };
    }

    /// <summary>
    /// Builds a descriptive summary for a tool group like "3개 파일 읽음, 2개의 패턴 검색됨".
    /// </summary>
    public static string BuildDescriptiveSummary(List<ContentPart> toolParts)
    {
        var grouped = new Dictionary<string, int>();
        foreach (var part in toolParts)
        {
            var name = NormalizeToolName(part.ToolCall?.Name ?? "Tool");
            grouped[name] = grouped.GetValueOrDefault(name) + 1;
        }

        var segments = new List<string>();
        foreach (var (name, count) in grouped)
        {
            segments.Add(name switch
            {
                "Read" => $"{count}개 파일 읽음",
                "Write" => count == 1 ? "1개 파일 작성됨" : $"{count}개 파일 작성됨",
                "Edit" => count == 1 ? "1개 파일 수정됨" : $"{count}개 파일 수정됨",
                "Grep" => count == 1 ? "패턴 검색됨" : $"{count}개의 패턴 검색됨",
                "Glob" => "파일 검색됨",
                "Bash" => count == 1 ? "명령 실행됨" : $"명령 {count}회 실행됨",
                "Agent" => count == 1 ? "에이전트 실행됨" : $"에이전트 {count}회 실행됨",
                "WebFetch" => count == 1 ? "웹 페이지 조회됨" : $"웹 페이지 {count}개 조회됨",
                "WebSearch" => count == 1 ? "웹 검색됨" : $"웹 검색 {count}회",
                "NotebookEdit" => count == 1 ? "노트북 수정됨" : $"노트북 {count}회 수정됨",
                "TodoWrite" => "할일 목록 업데이트됨",
                _ => count > 1 ? $"{name} {count}회" : name
            });
        }

        return string.Join(", ", segments);
    }

    private static string? ExtractHeaderDetail(string name, string? input)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(input);
            var root = doc.RootElement;

            return name switch
            {
                "Read" or "Write" or "Edit" => GetFilePath(root),
                "Bash" => GetStringProperty(root, "command", 60),
                "Grep" => GetStringProperty(root, "pattern", 50),
                "Glob" => GetStringProperty(root, "pattern", 50),
                "Agent" => GetAgentDescription(root),
                "WebFetch" => GetStringProperty(root, "url", 60),
                "WebSearch" => GetStringProperty(root, "query", 50),
                "NotebookEdit" => GetFilePath(root),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? GetFilePath(JsonElement root)
    {
        string? path = null;
        if (root.TryGetProperty("file_path", out var fp))
            path = fp.GetString();
        else if (root.TryGetProperty("path", out var p))
            path = p.GetString();

        if (string.IsNullOrEmpty(path))
            return null;

        // Show filename with parent directory for context
        var normalized = path.Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 2
            ? normalized
            : string.Join("/", parts[^2..]);
    }

    private static string? GetStringProperty(JsonElement root, string property, int maxLength)
    {
        if (!root.TryGetProperty(property, out var value))
            return null;

        var text = value.GetString();
        if (string.IsNullOrEmpty(text))
            return null;

        // Single line only
        var firstLine = text.Split('\n')[0].Trim();
        return firstLine.Length <= maxLength
            ? firstLine
            : firstLine[..maxLength] + "…";
    }

    private static string? GetAgentDescription(JsonElement root)
    {
        // Try description first, then prompt
        if (root.TryGetProperty("description", out var desc))
        {
            var text = desc.GetString();
            if (!string.IsNullOrEmpty(text))
                return text.Length <= 50 ? text : text[..50] + "…";
        }

        if (root.TryGetProperty("prompt", out var prompt))
        {
            var text = prompt.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                var firstLine = text.Split('\n')[0].Trim();
                return firstLine.Length <= 50 ? firstLine : firstLine[..50] + "…";
            }
        }

        return null;
    }

    private static string? GetReadHint(string output)
    {
        var lineCount = CountLines(output);
        return lineCount > 0 ? $"{lineCount}줄 읽음" : null;
    }

    private static string? GetGrepHint(string output)
    {
        var lineCount = CountLines(output);
        if (lineCount <= 0) return null;
        // Grep output: each non-empty line is typically a match or file path
        return $"{lineCount}개 결과";
    }

    private static string? GetGlobHint(string output)
    {
        var lineCount = CountLines(output);
        if (lineCount <= 0) return null;
        return $"{lineCount}개 파일 발견";
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        int count = 0;
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            if (!line.IsWhiteSpace())
                count++;
        }
        return count;
    }

    private static string NormalizeToolName(string name) => name.ToLowerInvariant() switch
    {
        "read" or "read_file" => "Read",
        "write" or "write_file" => "Write",
        "edit" or "edit_file" => "Edit",
        "bash" or "execute_bash" => "Bash",
        "glob" => "Glob",
        "grep" => "Grep",
        "agent" => "Agent",
        "webfetch" or "web_fetch" => "WebFetch",
        "websearch" or "web_search" => "WebSearch",
        "notebookedit" or "notebook_edit" => "NotebookEdit",
        "todowrite" or "todo_write" => "TodoWrite",
        _ when name.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase) => ExtractMcpToolName(name),
        _ => name
    };

    private static string ExtractMcpToolName(string name)
    {
        // mcp__serverId__toolName → extract toolName
        var parts = name.Split("__");
        return parts.Length >= 3 ? parts[^1] : name;
    }
}
