using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public static class QuestionDetector
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
                return (true, ["네, 진행해주세요", "아니요", "다른 방법으로"]);
        }

        foreach (var pattern in ChoicePatterns)
        {
            if (lower.Contains(pattern))
                return (true, ["첫 번째", "두 번째", "설명해주세요"]);
        }

        // Generic question
        return (true, ["네", "아니요"]);
    }
}
