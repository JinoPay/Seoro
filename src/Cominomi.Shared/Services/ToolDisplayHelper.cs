using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cominomi.Shared.Models;
using Cominomi.Shared.Resources;
using MudBlazor;

namespace Cominomi.Shared.Services;

public static class ToolDisplayHelper
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    ///     Builds a descriptive summary for a tool group.
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
            segments.Add(name switch
            {
                "Read" => Strings.Tool_FilesRead(count),
                "Write" => count == 1 ? Strings.Tool_FileWrittenSingle : Strings.Tool_FileWrittenMultiple(count),
                "Edit" => count == 1 ? Strings.Tool_FileEditedSingle : Strings.Tool_FileEditedMultiple(count),
                "Grep" => count == 1 ? Strings.Tool_GrepSingle : Strings.Tool_GrepMultiple(count),
                "Glob" => Strings.Tool_GlobDone,
                "Bash" => count == 1 ? Strings.Tool_BashSingle : Strings.Tool_BashMultiple(count),
                "Agent" => count == 1 ? Strings.Tool_AgentSingle : Strings.Tool_AgentMultiple(count),
                "WebFetch" => count == 1 ? Strings.Tool_WebFetchSingle : Strings.Tool_WebFetchMultiple(count),
                "WebSearch" => count == 1 ? Strings.Tool_WebSearchSingle : Strings.Tool_WebSearchMultiple(count),
                "NotebookEdit" => count == 1 ? Strings.Tool_NotebookSingle : Strings.Tool_NotebookMultiple(count),
                "TodoWrite" => Strings.Tool_TodoWriteDone,
                "AskUserQuestion" => "질문 대기 중",
                _ => count > 1 ? Strings.Tool_DefaultMultiple(name, count) : name
            });

        return string.Join(", ", segments);
    }

    /// <summary>
    ///     Pretty-prints JSON with Unicode characters properly rendered (not \uXXXX escaped).
    ///     Falls back to the original string if parsing fails.
    /// </summary>
    public static string FormatJson(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return json ?? "";

        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, PrettyJsonOptions);
        }
        catch
        {
            return json;
        }
    }

    /// <summary>
    ///     Returns a contextual header label like "Read MessageBubble.razor" or "Grep pattern".
    /// </summary>
    public static string GetHeaderLabel(ToolCall tool)
    {
        var name = NormalizeToolName(tool.Name);
        var detail = ExtractHeaderDetail(name, tool.Input);
        return detail != null ? $"{name} {detail}" : name;
    }

    /// <summary>
    ///     Returns a localized streaming label like "파일 읽는 중..." for the given tool name.
    /// </summary>
    public static string GetStreamingLabel(string? name)
    {
        var normalized = NormalizeToolName(name ?? "");
        return normalized switch
        {
            "Bash" => "명령 실행 중...",
            "Read" => "파일 읽는 중...",
            "Write" => "파일 작성 중...",
            "Edit" => "파일 수정 중...",
            "Glob" => "파일 검색 중...",
            "Grep" => "내용 검색 중...",
            "Agent" => "에이전트 실행 중...",
            "WebFetch" => "웹 페이지 가져오는 중...",
            "WebSearch" => "웹 검색 중...",
            "NotebookEdit" => "노트북 수정 중...",
            "TodoWrite" => "할일 목록 업데이트 중...",
            "AskUserQuestion" => "질문 대기 중...",
            "Skill" => "스킬 실행 중...",
            "ToolSearch" => "도구 검색 중...",
            _ => $"{normalized} 사용 중..."
        };
    }

    /// <summary>
    ///     Returns a Material Design icon string for the given tool name.
    /// </summary>
    public static string GetToolIcon(string? name)
    {
        var normalized = NormalizeToolName(name ?? "");
        return normalized switch
        {
            "Bash" => Icons.Material.Filled.Terminal,
            "Read" => Icons.Material.Filled.Description,
            "Write" => Icons.Material.Filled.Edit,
            "Edit" => Icons.Material.Filled.EditNote,
            "Glob" => Icons.Material.Filled.Search,
            "Grep" => Icons.Material.Filled.FindInPage,
            "Agent" => Icons.Material.Filled.SmartToy,
            "WebFetch" => Icons.Material.Filled.Language,
            "WebSearch" => Icons.Material.Filled.TravelExplore,
            "NotebookEdit" => Icons.Material.Filled.DataObject,
            "TodoWrite" => Icons.Material.Filled.Checklist,
            "AskUserQuestion" => Icons.Material.Filled.QuestionAnswer,
            "Skill" => Icons.Material.Filled.AutoAwesome,
            "ToolSearch" => Icons.Material.Filled.ManageSearch,
            _ when (name ?? "").StartsWith("mcp__", StringComparison.OrdinalIgnoreCase)
                => Icons.Material.Filled.Hub,
            _ => Icons.Material.Filled.Extension
        };
    }

    /// <summary>
    ///     Returns a compact result hint. Returns null if no meaningful hint is available.
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
            "Agent" => GetAgentHint(tool.Output),
            _ => null
        };
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var count = 0;
        foreach (var line in text.AsSpan().EnumerateLines())
            if (!line.IsWhiteSpace())
                count++;
        return count;
    }

    private static string ExtractMcpToolName(string name)
    {
        // mcp__serverId__toolName → extract toolName
        var parts = name.Split("__");
        return parts.Length >= 3 ? parts[^1] : name;
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
                "AskUserQuestion" => GetAskUserHeader(root),
                _ => null
            };
        }
        catch
        {
            return null;
        }
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

    private static string? GetAgentHint(string output)
    {
        if (string.IsNullOrEmpty(output)) return null;

        var toolUsesMatch = Regex.Match(output, @"tool_uses:\s*(\d+)");
        var durationMatch = Regex.Match(output, @"duration_ms:\s*(\d+)");

        if (!toolUsesMatch.Success) return null;

        var parts = new List<string> { $"{toolUsesMatch.Groups[1].Value}개 도구 사용" };

        if (durationMatch.Success && long.TryParse(durationMatch.Groups[1].Value, out var ms))
        {
            var sec = ms / 1000;
            parts.Add(sec < 60 ? $"{sec}초" : $"{sec / 60}분 {sec % 60}초");
        }

        return string.Join(" | ", parts);
    }

    private static string? GetAskUserHeader(JsonElement root)
    {
        if (!root.TryGetProperty("questions", out var questions) ||
            questions.ValueKind != JsonValueKind.Array ||
            questions.GetArrayLength() == 0)
            return null;

        var first = questions[0];
        // Prefer header, fall back to question text
        if (first.TryGetProperty("header", out var header))
        {
            var text = header.GetString();
            if (!string.IsNullOrEmpty(text))
                return text.Length <= 50 ? text : text[..50] + "…";
        }

        if (first.TryGetProperty("question", out var question))
        {
            var text = question.GetString();
            if (!string.IsNullOrEmpty(text))
                return text.Length <= 50 ? text : text[..50] + "…";
        }

        return null;
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

    private static string? GetGlobHint(string output)
    {
        var lineCount = CountLines(output);
        if (lineCount <= 0) return null;
        return Strings.Tool_GlobHint(lineCount);
    }

    private static string? GetGrepHint(string output)
    {
        var lineCount = CountLines(output);
        if (lineCount <= 0) return null;
        return Strings.Tool_GrepHint(lineCount);
    }

    private static string? GetReadHint(string output)
    {
        var lineCount = CountLines(output);
        return lineCount > 0 ? Strings.Tool_ReadHint(lineCount) : null;
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

    internal static string NormalizeToolName(string name)
    {
        return name.ToLowerInvariant() switch
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
            "askuserquestion" or "ask_user_question" => "AskUserQuestion",
            "skill" => "Skill",
            "toolsearch" or "tool_search" => "ToolSearch",
            _ when name.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase) => ExtractMcpToolName(name),
            _ => name
        };
    }
}