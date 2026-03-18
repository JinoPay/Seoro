using Cominomi.Shared.Models;
using Cominomi.Shared.Services;

namespace Cominomi.Shared.Tests;

public class ContentGrouperTests
{
    private static ContentPart TextPart(string text) =>
        new() { Type = ContentPartType.Text, Text = text };

    private static ContentPart ToolPart(string name = "Read", string input = "{}") =>
        new()
        {
            Type = ContentPartType.ToolCall,
            ToolCall = new ToolCall { Id = Guid.NewGuid().ToString(), Name = name, Input = input }
        };

    private static ContentPart ThinkingPart(string text) =>
        new() { Type = ContentPartType.Thinking, Text = text };

    [Fact]
    public void Group_EmptyParts_ReturnsEmpty()
    {
        var result = ContentGrouper.Group([], isStreaming: false);
        Assert.Empty(result);
    }

    [Fact]
    public void Group_TextOnly_ReturnsFinalText()
    {
        var parts = new List<ContentPart> { TextPart("Hello world") };
        var result = ContentGrouper.Group(parts, isStreaming: false);

        Assert.Single(result);
        Assert.Equal(ContentGroupType.FinalText, result[0].Type);
        Assert.False(result[0].IsIntermediate);
    }

    [Fact]
    public void Group_ToolsOnly_ReturnsToolGroup()
    {
        var parts = new List<ContentPart>
        {
            ToolPart("Read"),
            ToolPart("Grep")
        };
        var result = ContentGrouper.Group(parts, isStreaming: false);

        Assert.Single(result);
        Assert.Equal(ContentGroupType.ToolGroup, result[0].Type);
        Assert.Equal(2, result[0].Parts.Count);
    }

    [Fact]
    public void Group_ToolsThenText_TextIsFinalText()
    {
        var parts = new List<ContentPart>
        {
            ToolPart("Read"),
            TextPart("Here is the analysis result.")
        };
        var result = ContentGrouper.Group(parts, isStreaming: false);

        Assert.Equal(2, result.Count);
        Assert.Equal(ContentGroupType.ToolGroup, result[0].Type);
        Assert.Equal(ContentGroupType.FinalText, result[1].Type);
    }

    [Fact]
    public void Group_IntermediateTextBetweenTools_MarkedAsIntermediate()
    {
        var parts = new List<ContentPart>
        {
            ToolPart("Read"),
            TextPart("확인해보겠습니다"),
            ToolPart("Grep")
        };
        var result = ContentGrouper.Group(parts, isStreaming: false);

        Assert.Equal(3, result.Count);
        Assert.Equal(ContentGroupType.ToolGroup, result[0].Type);
        var textGroup = result[1];
        Assert.Equal(ContentGroupType.Text, textGroup.Type);
        Assert.True(textGroup.IsIntermediate);
    }

    [Fact]
    public void Group_IntermediateEnglishText_MarkedAsIntermediate()
    {
        var parts = new List<ContentPart>
        {
            ToolPart("Read"),
            TextPart("Let me check the file"),
            ToolPart("Grep")
        };
        var result = ContentGrouper.Group(parts, isStreaming: false);

        var textGroup = result[1];
        Assert.True(textGroup.IsIntermediate);
    }

    [Fact]
    public void Group_LastTextBeforeTools_LongWithoutPattern_BecomesFinalText()
    {
        // When the only text is the last text and has tools after it,
        // it needs BOTH <=80 chars AND intermediate pattern to collapse.
        // Without pattern match, it becomes FinalText.
        var parts = new List<ContentPart>
        {
            ToolPart("Read"),
            TextPart("This is a substantive analysis of the code architecture."),
            ToolPart("Write")
        };
        var result = ContentGrouper.Group(parts, isStreaming: false);

        var textGroup = result[1];
        Assert.Equal(ContentGroupType.FinalText, textGroup.Type);
        Assert.False(textGroup.IsIntermediate);
    }

    [Fact]
    public void Group_ThinkingBlocks_SeparatedCorrectly()
    {
        var parts = new List<ContentPart>
        {
            ThinkingPart("Let me think about this..."),
            ToolPart("Read"),
            TextPart("Done.")
        };
        var result = ContentGrouper.Group(parts, isStreaming: false);

        Assert.Equal(3, result.Count);
        Assert.Equal(ContentGroupType.Thinking, result[0].Type);
        Assert.Equal(ContentGroupType.ToolGroup, result[1].Type);
        Assert.Equal(ContentGroupType.FinalText, result[2].Type);
    }

    [Fact]
    public void Group_EmptyTextParts_Ignored()
    {
        var parts = new List<ContentPart>
        {
            TextPart(""),
            ToolPart("Read"),
            TextPart("Result")
        };
        var result = ContentGrouper.Group(parts, isStreaming: false);

        Assert.Equal(2, result.Count);
        Assert.Equal(ContentGroupType.ToolGroup, result[0].Type);
        Assert.Equal(ContentGroupType.FinalText, result[1].Type);
    }

    [Fact]
    public void Group_ConsecutiveTools_GroupedTogether()
    {
        var parts = new List<ContentPart>
        {
            ToolPart("Read"),
            ToolPart("Grep"),
            ToolPart("Glob"),
            TextPart("Found it.")
        };
        var result = ContentGrouper.Group(parts, isStreaming: false);

        Assert.Equal(2, result.Count);
        Assert.Equal(ContentGroupType.ToolGroup, result[0].Type);
        Assert.Equal(3, result[0].Parts.Count);
    }

    [Fact]
    public void Group_ToolGroupHasSummary()
    {
        var parts = new List<ContentPart>
        {
            ToolPart("Read"),
            ToolPart("Edit")
        };
        var result = ContentGrouper.Group(parts, isStreaming: false);

        Assert.NotEmpty(result[0].Summary);
    }

    [Fact]
    public void Group_LastTextBeforeTools_ShortIntermediate_Collapsed()
    {
        // When the last text is short and matches intermediate patterns, and has tools after
        var parts = new List<ContentPart>
        {
            TextPart("살펴보겠습니다"),
            ToolPart("Read")
        };
        var result = ContentGrouper.Group(parts, isStreaming: false);

        // Text before tool with intermediate pattern
        var textGroup = result.First(g => g.Type == ContentGroupType.Text);
        Assert.True(textGroup.IsIntermediate);
    }

    [Fact]
    public void Group_NonLastVerboseText_NumberedList_IsIntermediate()
    {
        // When verbose text is NOT the last text (another text follows), the non-last
        // classification applies: <=150 chars OR intermediate pattern OR verbose → intermediate
        var text = "Plan:\n1. Read the file\n2. Analyze structure\n3. Make changes\n4. Test";
        var parts = new List<ContentPart>
        {
            ToolPart("Read"),
            TextPart(text),
            ToolPart("Write"),
            TextPart("Done with the changes.")
        };
        var result = ContentGrouper.Group(parts, isStreaming: false);

        // text at index 1 is non-last, short (<=150), between tools → intermediate
        var textGroup = result[1];
        Assert.True(textGroup.IsIntermediate);
    }

    [Fact]
    public void BuildActivitySummary_CountsCorrectly()
    {
        var groups = new List<ContentGroup>
        {
            new()
            {
                Type = ContentGroupType.ToolGroup,
                Parts =
                [
                    ToolPart("Read"),
                    new() { Type = ContentPartType.ToolCall, ToolCall = new ToolCall { Name = "Edit", IsError = true } }
                ]
            },
            new() { Type = ContentGroupType.Thinking, Parts = [ThinkingPart("thinking")] },
            new() { Type = ContentGroupType.Text, Parts = [TextPart("text")] }
        };

        var summary = ContentGrouper.BuildActivitySummary(groups);

        Assert.Equal(2, summary.TotalToolCalls);
        Assert.True(summary.HasErrors);
        Assert.Equal(1, summary.ThinkingBlocks);
        Assert.Equal(1, summary.TextSegments);
    }
}
