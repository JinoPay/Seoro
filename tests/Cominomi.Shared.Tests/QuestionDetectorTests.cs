using Cominomi.Shared.Models;
using Cominomi.Shared.Services;

namespace Cominomi.Shared.Tests;

public class QuestionDetectorTests
{
    private static ChatMessage AssistantMessage(string text) =>
        new() { Role = MessageRole.Assistant, Text = text };

    [Fact]
    public void Detect_NullMessage_ReturnsFalse()
    {
        var (isQuestion, responses) = QuestionDetector.Detect(null);
        Assert.False(isQuestion);
        Assert.Empty(responses);
    }

    [Fact]
    public void Detect_UserMessage_ReturnsFalse()
    {
        var msg = new ChatMessage { Role = MessageRole.User, Text = "진행할까요?" };
        var (isQuestion, _) = QuestionDetector.Detect(msg);
        Assert.False(isQuestion);
    }

    [Fact]
    public void Detect_NoQuestionMark_ReturnsFalse()
    {
        var msg = AssistantMessage("작업을 완료했습니다.");
        var (isQuestion, _) = QuestionDetector.Detect(msg);
        Assert.False(isQuestion);
    }

    [Fact]
    public void Detect_EmptyText_ReturnsFalse()
    {
        var msg = AssistantMessage("");
        var (isQuestion, _) = QuestionDetector.Detect(msg);
        Assert.False(isQuestion);
    }

    [Fact]
    public void Detect_KoreanYesNoPattern_ReturnsYesNoResponses()
    {
        var msg = AssistantMessage("이 변경사항을 적용할까요?");
        var (isQuestion, responses) = QuestionDetector.Detect(msg);

        Assert.True(isQuestion);
        Assert.Equal(3, responses.Count);
        Assert.Contains("네, 진행해주세요", responses);
        Assert.Contains("아니요", responses);
        Assert.Contains("다른 방법으로", responses);
    }

    [Fact]
    public void Detect_EnglishYesNoPattern_ReturnsYesNoResponses()
    {
        var msg = AssistantMessage("Should I proceed with this change?");
        var (isQuestion, responses) = QuestionDetector.Detect(msg);

        Assert.True(isQuestion);
        Assert.Equal(3, responses.Count);
        Assert.Contains("네, 진행해주세요", responses);
    }

    [Fact]
    public void Detect_ChoicePatternWithNumberedList_ExtractsChoices()
    {
        var msg = AssistantMessage(
            "어떤 것을 선택하시겠습니까?\n1. React로 구현\n2. Vue로 구현\n3. Svelte로 구현?");
        var (isQuestion, responses) = QuestionDetector.Detect(msg);

        Assert.True(isQuestion);
        Assert.True(responses.Count >= 2);
    }

    [Fact]
    public void Detect_ChoicePatternWithBulletList_ExtractsChoices()
    {
        var msg = AssistantMessage(
            "어떤 것을 사용할까요?\n- **TypeScript**\n- **JavaScript**?");
        var (isQuestion, responses) = QuestionDetector.Detect(msg);

        Assert.True(isQuestion);
        Assert.True(responses.Count >= 2);
    }

    [Fact]
    public void Detect_EnglishChoicePattern_ReturnsChoices()
    {
        var msg = AssistantMessage("Which approach should we use?");
        var (isQuestion, responses) = QuestionDetector.Detect(msg);

        Assert.True(isQuestion);
        // Without extractable list, falls back to default choices
        Assert.True(responses.Count >= 2);
    }

    [Fact]
    public void Detect_GenericQuestion_ReturnsYesNo()
    {
        var msg = AssistantMessage("이해가 되시나요?");
        var (isQuestion, responses) = QuestionDetector.Detect(msg);

        Assert.True(isQuestion);
        Assert.Equal(2, responses.Count);
        Assert.Contains("네", responses);
        Assert.Contains("아니요", responses);
    }

    [Fact]
    public void Detect_MultipleYesNoPatterns_MatchesFirst()
    {
        var msg = AssistantMessage("계속할까요? 시작할까요?");
        var (isQuestion, responses) = QuestionDetector.Detect(msg);

        Assert.True(isQuestion);
        Assert.Equal(3, responses.Count);
    }
}
