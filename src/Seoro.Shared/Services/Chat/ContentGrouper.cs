using System.Text.Json;
using System.Text.RegularExpressions;
using Seoro.Shared.Models.ViewModels;

namespace Seoro.Shared.Services.Chat;

public static partial class ContentGrouper
{
    public static ActivitySummaryInfo BuildActivitySummary(List<ContentGroup> activityGroups)
    {
        var info = new ActivitySummaryInfo();
        var fileChanges = new Dictionary<string, string>();

        foreach (var group in activityGroups)
            switch (group.Type)
            {
                case ContentGroupType.ToolGroup:
                    foreach (var part in group.Parts)
                    {
                        info.TotalToolCalls++;
                        if (part.ToolCall?.IsError == true) info.HasErrors = true;
                        ExtractFilePath(part.ToolCall, fileChanges);
                    }

                    break;
                case ContentGroupType.Thinking:
                    info.ThinkingBlocks++;
                    break;
                case ContentGroupType.Text:
                    info.TextSegments++;
                    break;
            }

        info.FileChanges = fileChanges
            .Select(kv => new FileChangeInfo { FilePath = kv.Key, ToolAction = kv.Value })
            .ToList();

        return info;
    }

    public static List<ContentGroup> Group(List<ContentPart> parts, bool isStreaming)
    {
        if (parts.Count == 0)
            return [];

        var groups = new List<ContentGroup>();
        ContentGroup? currentToolGroup = null;

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];

            if (part.Type == ContentPartType.ToolCall && part.ToolCall != null)
            {
                // Skip child tool calls — they render inside their parent Agent widget
                if (part.ToolCall.ParentToolUseId != null)
                    continue;

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

    private static bool IsIntermediateText(string text)
    {
        // Language-agnostic structural heuristic:
        // Text containing code, markdown formatting, or links is substantive
        if (text.Contains('`') || text.Contains("](") || text.Contains("**") || text.Contains("## "))
            return false;

        // Multi-paragraph text likely contains substance
        if (text.Contains("\n\n"))
            return false;

        // Short, unformatted, single-block text is likely transitional
        return true;
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

    [GeneratedRegex(@"^[-*]\s", RegexOptions.Multiline)]
    private static partial Regex BulletListRegex();

    [GeneratedRegex(@"^\d+\.", RegexOptions.Multiline)]
    private static partial Regex NumberedListRegex();

    private static string BuildToolSummary(List<ContentPart> toolParts)
    {
        return ToolDisplayHelper.BuildDescriptiveSummary(toolParts);
    }

    private static void ClassifyTextGroups(List<ContentGroup> groups, bool isStreaming)
    {
        // Find the last text group index
        var lastTextIndex = -1;
        for (var i = groups.Count - 1; i >= 0; i--)
            if (groups[i].Type == ContentGroupType.Text)
            {
                lastTextIndex = i;
                break;
            }

        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.Type != ContentGroupType.Text)
                continue;

            var text = group.Parts[0].Text?.Trim() ?? "";
            var hasPrevTool = i > 0 && groups[i - 1].Type == ContentGroupType.ToolGroup;
            var hasNextTool = i < groups.Count - 1 && groups[i + 1].Type == ContentGroupType.ToolGroup;

            if (i == lastTextIndex)
            {
                if (hasNextTool)
                {
                    // More tool calls follow — only collapse if clearly filler
                    if (text.Length <= 80 && IsIntermediateText(text))
                        group.IsIntermediate = true;
                    else
                        group.Type = ContentGroupType.FinalText;
                }
                else
                {
                    // Final text after all tools — always show
                    group.Type = ContentGroupType.FinalText;
                }

                continue;
            }

            // Non-last text: check if intermediate (between tool groups or short/filler)
            if (hasPrevTool || hasNextTool)
                if (text.Length <= 150 || IsIntermediateText(text) || IsLikelyVerboseText(text))
                    group.IsIntermediate = true;
        }
    }

    private static void ExtractFilePath(ToolCall? tool, Dictionary<string, string> fileChanges)
    {
        if (tool == null || string.IsNullOrEmpty(tool.Input)) return;
        var name = ToolDisplayHelper.NormalizeToolName(tool.Name);
        if (name is not ("Edit" or "Write")) return;

        try
        {
            using var doc = JsonDocument.Parse(tool.Input);
            var root = doc.RootElement;

            string? path = null;
            if (root.TryGetProperty("file_path", out var fp))
                path = fp.GetString();
            else if (root.TryGetProperty("path", out var p))
                path = p.GetString();

            if (!string.IsNullOrEmpty(path))
                fileChanges.TryAdd(path, name);
        }
        catch
        {
            /* input may not be valid JSON */
        }
    }
}