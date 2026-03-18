using System.Text.RegularExpressions;
using Cominomi.Shared.Models;
using Cominomi.Shared.Resources;

namespace Cominomi.Shared.Services;

public static partial class QuestionDetector
{
    private static readonly string[] YesNoPatterns =
    [
        "할까요", "진행할까", "시작할까", "계속할까", "수정할까",
        "변경할까", "삭제할까", "추가할까", "적용할까", "실행할까",
        "shall i", "should i", "do you want", "would you like",
        "can i", "may i", "want me to", "proceed", "continue"
    ];

    private static readonly string[] ChoicePatterns =
    [
        "어떤 것", "어느 것", "무엇을", "어떻게",
        "which", "what approach", "what method", "how should"
    ];

    [GeneratedRegex(@"^\s*(\d+)[.)]\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex NumberedListRegex();

    [GeneratedRegex(@"^\s*[-*]\s+\*{0,2}(.+?)\*{0,2}\s*$", RegexOptions.Multiline)]
    private static partial Regex BulletListRegex();

    public static (bool IsQuestion, List<string> SuggestedResponses) Detect(ChatMessage? message)
    {
        if (message == null || message.Role != MessageRole.Assistant)
            return (false, []);

        var text = message.Text?.TrimEnd() ?? "";
        if (string.IsNullOrEmpty(text) || !text.EndsWith('?'))
            return (false, []);

        var lower = text.ToLowerInvariant();

        foreach (var pattern in YesNoPatterns)
        {
            if (lower.Contains(pattern))
                return (true, [Strings.Suggest_Proceed, Strings.Suggest_No, Strings.Suggest_Alternative]);
        }

        foreach (var pattern in ChoicePatterns)
        {
            if (lower.Contains(pattern))
            {
                var extracted = ExtractChoices(text);
                if (extracted.Count >= 2)
                    return (true, extracted);
                return (true, [Strings.Suggest_First, Strings.Suggest_Second, Strings.Suggest_Explain]);
            }
        }

        // Generic question
        return (true, [Strings.Suggest_Yes, Strings.Suggest_No]);
    }

    private static List<string> ExtractChoices(string text)
    {
        // Try numbered list first (1. xxx, 2. xxx)
        var numbered = NumberedListRegex().Matches(text);
        if (numbered.Count >= 2)
        {
            return numbered
                .Select(m => m.Groups[2].Value.Trim().TrimEnd('.', ','))
                .Select(StripMarkdownBold)
                .Where(s => s.Length > 0 && s.Length <= 60)
                .ToList();
        }

        // Try bullet list (- xxx, * xxx)
        var bullets = BulletListRegex().Matches(text);
        if (bullets.Count >= 2)
        {
            return bullets
                .Select(m => m.Groups[1].Value.Trim().TrimEnd('.', ','))
                .Select(StripMarkdownBold)
                .Where(s => s.Length > 0 && s.Length <= 60)
                .ToList();
        }

        return [];
    }

    private static string StripMarkdownBold(string s)
    {
        // Remove **bold** markers
        if (s.StartsWith("**") && s.EndsWith("**") && s.Length > 4)
            return s[2..^2];
        return s;
    }
}
