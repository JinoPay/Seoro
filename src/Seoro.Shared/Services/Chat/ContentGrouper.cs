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
                // 자식 도구 호출 건너뛰기 — 부모 Agent 위젯 내에서 렌더링됨
                if (part.ToolCall.ParentToolUseId != null)
                    continue;

                // TodoWrite 인라인 렌더링 건너뛰기 — TodoFloater(좌하단 토글)에서만 표시
                if (TodoSnapshotParser.IsTodoWriteTool(part.ToolCall.Name))
                    continue;

                currentToolGroup ??= new ContentGroup { Type = ContentGroupType.ToolGroup };
                currentToolGroup.Parts.Add(part);
            }
            else if (part.Type == ContentPartType.Thinking && !string.IsNullOrEmpty(part.Text))
            {
                // 대기 중인 도구 그룹 플러시
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
                // 대기 중인 도구 그룹 플러시
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

        // 최종 도구 그룹 플러시
        if (currentToolGroup != null)
        {
            currentToolGroup.Summary = BuildToolSummary(currentToolGroup.Parts);
            groups.Add(currentToolGroup);
        }

        // 후처리: 중간 텍스트와 최종 텍스트 표시
        ClassifyTextGroups(groups, isStreaming);

        return groups;
    }

    private static bool IsIntermediateText(string text)
    {
        // 언어 중립적인 구조적 휴리스틱:
        // 코드, 마크다운 형식 또는 링크가 포함된 텍스트는 실질적임
        if (text.Contains('`') || text.Contains("](") || text.Contains("**") || text.Contains("## "))
            return false;

        // 여러 단락의 텍스트는 실질적인 내용을 포함할 가능성이 높음
        if (text.Contains("\n\n"))
            return false;

        // 짧고 형식이 없는 단일 블록 텍스트는 과도기적일 가능성이 높음
        return true;
    }

    private static bool IsLikelyVerboseText(string text)
    {
        // 매우 긴 텍스트는 장황한 계획/설명일 가능성이 높음
        if (text.Length > 400) return true;

        // 3개 이상의 항목이 있는 번호 매김 목록
        if (NumberedListRegex().Count(text) >= 3) return true;

        // 3개 이상의 항목이 있는 글머리 목록
        if (BulletListRegex().Count(text) >= 3) return true;

        // 많은 줄은 계획/명령 텍스트를 의미함
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
        // 마지막 텍스트 그룹 인덱스 찾기
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
                    // 더 많은 도구 호출이 이어짐 — 명확한 채우기인 경우에만 축소
                    if (text.Length <= 80 && IsIntermediateText(text))
                        group.IsIntermediate = true;
                    else
                        group.Type = ContentGroupType.FinalText;
                }
                else
                {
                    // 모든 도구 후 최종 텍스트 — 항상 표시
                    group.Type = ContentGroupType.FinalText;
                }

                continue;
            }

            // 마지막이 아닌 텍스트: 중간 여부 확인 (도구 그룹 사이 또는 짧음/채우기)
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
            /* 입력이 유효한 JSON이 아닐 수 있음 */
        }
    }
}