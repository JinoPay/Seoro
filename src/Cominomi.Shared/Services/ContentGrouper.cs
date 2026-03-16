using System.Text.RegularExpressions;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public static partial class ContentGrouper
{
    private static readonly string[] IntermediatePatterns =
    [
        "확인", "살펴보", "읽어보", "검색", "찾아보", "분석",
        "조사", "탐색", "코드베이스", "파일을", "디렉토리",
        "let me", "i'll", "i will", "let's", "looking at",
        "checking", "reading", "searching", "examining",
        "explore", "investigate", "review", "understand"
    ];

    [GeneratedRegex(@"^\d+\.", RegexOptions.Multiline)]
    private static partial Regex NumberedListRegex();

    [GeneratedRegex(@"^[-*]\s", RegexOptions.Multiline)]
    private static partial Regex BulletListRegex();

    public static List<ContentGroup> Group(List<ContentPart> parts, bool isStreaming)
    {
        if (parts.Count == 0)
            return [];

        var groups = new List<ContentGroup>();
        ContentGroup? currentToolGroup = null;

        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];

            if (part.Type == ContentPartType.ToolCall && part.ToolCall != null)
            {
                currentToolGroup ??= new ContentGroup { Type = ContentGroupType.ToolGroup };
                currentToolGroup.Parts.Add(part);
            }
            else if (part.Type == ContentPartType.Thinking && !string.IsNullOrEmpty(part.Text))
            {
                // Flush any pending tool group
                if (currentToolGroup != null)
                {
                    currentToolGroup.Summary = BuildToolSummary(currentToolGroup.Parts);
                    groups.Add(currentToolGroup);
                    currentToolGroup = null;
                }

                groups.Add(new ContentGroup
                {
                    Type = ContentGroupType.Thinking,
                    Parts = [part]
                });
            }
            else if (part.Type == ContentPartType.Text && !string.IsNullOrEmpty(part.Text))
            {
                // Flush any pending tool group
                if (currentToolGroup != null)
                {
                    currentToolGroup.Summary = BuildToolSummary(currentToolGroup.Parts);
                    groups.Add(currentToolGroup);
                    currentToolGroup = null;
                }

                groups.Add(new ContentGroup
                {
                    Type = ContentGroupType.Text,
                    Parts = [part]
                });
            }
        }

        // Flush final tool group
        if (currentToolGroup != null)
        {
            currentToolGroup.Summary = BuildToolSummary(currentToolGroup.Parts);
            groups.Add(currentToolGroup);
        }

        // Post-process: mark intermediate text and final text
        ClassifyTextGroups(groups, isStreaming);

        return groups;
    }

    private static void ClassifyTextGroups(List<ContentGroup> groups, bool isStreaming)
    {
        // Find the last text group index
        int lastTextIndex = -1;
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i].Type == ContentGroupType.Text)
            {
                lastTextIndex = i;
                break;
            }
        }

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.Type != ContentGroupType.Text)
                continue;

            var text = group.Parts[0].Text?.Trim() ?? "";
            bool hasPrevTool = i > 0 && groups[i - 1].Type == ContentGroupType.ToolGroup;
            bool hasNextTool = i < groups.Count - 1 && groups[i + 1].Type == ContentGroupType.ToolGroup;

            if (i == lastTextIndex)
            {
                // During streaming: if this looks like verbose pre-tool text, collapse it
                if (isStreaming && hasPrevTool && IsLikelyVerboseText(text))
                {
                    group.IsIntermediate = true;
                }
                // After streaming: also collapse if adjacent to tools and verbose
                else if (!isStreaming && (hasPrevTool || hasNextTool) &&
                         (text.Length <= 150 || IsIntermediateText(text) || IsLikelyVerboseText(text)))
                {
                    group.IsIntermediate = true;
                }
                else
                {
                    group.Type = ContentGroupType.FinalText;
                }
                continue;
            }

            // Non-last text: check if intermediate (between tool groups or short/filler)
            if (hasPrevTool || hasNextTool)
            {
                if (text.Length <= 150 || IsIntermediateText(text) || IsLikelyVerboseText(text))
                {
                    group.IsIntermediate = true;
                }
            }
        }
    }

    private static bool IsIntermediateText(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var pattern in IntermediatePatterns)
        {
            if (lower.Contains(pattern))
                return true;
        }
        return false;
    }

    private static bool IsLikelyVerboseText(string text)
    {
        // Very long text is likely verbose planning/description
        if (text.Length > 400) return true;

        // Numbered lists with 3+ items
        if (NumberedListRegex().Count(text) >= 3) return true;

        // Bullet lists with 3+ items
        if (BulletListRegex().Count(text) >= 3) return true;

        // Many lines suggest planning/instruction text
        if (text.Split('\n').Length >= 6) return true;

        return false;
    }

    private static string BuildToolSummary(List<ContentPart> toolParts)
    {
        var counts = new Dictionary<string, int>();
        foreach (var part in toolParts)
        {
            var name = NormalizeToolName(part.ToolCall?.Name ?? "Tool");
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        var summaryParts = counts.Select(kv => kv.Value > 1 ? $"{kv.Key} {kv.Value}회" : kv.Key);
        return string.Join(", ", summaryParts);
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
        _ => name
    };
}
