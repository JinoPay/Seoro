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

    private static readonly string[] ConfirmPatterns =
    [
        "확인해 주세요", "확인해주세요", "확인 부탁", "알려주세요", "알려 주세요",
        "선택해 주세요", "선택해주세요", "골라 주세요", "골라주세요",
        "결정해 주세요", "결정해주세요", "답변 부탁",
        "please confirm", "let me know", "please select", "please choose",
        "please pick", "pick one", "choose one", "select one"
    ];

    private static readonly string[] ImperativeQuestionPatterns =
    [
        "괜찮을까", "될까", "맞을까", "좋을까", "나을까",
        "괜찮겠", "되겠", "맞겠", "좋겠",
        "을까요", "ㄹ까요", "는지요", "인지요", "건지요",
        "is that ok", "is this ok", "does that work", "sound good",
        "make sense", "look good", "that correct", "is that right"
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
        if (string.IsNullOrEmpty(text))
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

        foreach (var pattern in ConfirmPatterns)
        {
            if (lower.Contains(pattern))
                return (true, [Strings.Suggest_Proceed, Strings.Suggest_No, Strings.Suggest_Alternative]);
        }

        foreach (var pattern in ImperativeQuestionPatterns)
        {
            if (lower.Contains(pattern))
                return (true, [Strings.Suggest_Yes, Strings.Suggest_No]);
        }

        // Generic question ending with ?
        if (text.EndsWith('?'))
            return (true, [Strings.Suggest_Yes, Strings.Suggest_No]);

        return (false, []);
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
