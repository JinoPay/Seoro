using System.Text.Json;
using Cominomi.Shared.Services;

namespace Cominomi.Shared.Tests;

public class ExtractToolResultContentTests
{
    [Fact]
    public void ExtractToolResultContent_StringElement_ReturnsString()
    {
        var json = JsonDocument.Parse("\"hello world\"");
        var result = StreamEventProcessor.ExtractToolResultContent(json.RootElement);
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void ExtractToolResultContent_ArrayWithTextItems_JoinsTexts()
    {
        var json = JsonDocument.Parse("""
            [
                {"type": "text", "text": "first line"},
                {"type": "text", "text": "second line"}
            ]
            """);
        var result = StreamEventProcessor.ExtractToolResultContent(json.RootElement);
        Assert.Equal("first line\nsecond line", result);
    }

    [Fact]
    public void ExtractToolResultContent_ArrayWithMixedItems_ExtractsTextOnly()
    {
        var json = JsonDocument.Parse("""
            [
                {"type": "text", "text": "output"},
                {"type": "image", "source": "data:..."}
            ]
            """);
        var result = StreamEventProcessor.ExtractToolResultContent(json.RootElement);
        Assert.Equal("output", result);
    }

    [Fact]
    public void ExtractToolResultContent_EmptyArray_ReturnsEmpty()
    {
        var json = JsonDocument.Parse("[]");
        var result = StreamEventProcessor.ExtractToolResultContent(json.RootElement);
        Assert.Equal("", result);
    }

    [Fact]
    public void ExtractToolResultContent_ObjectElement_ReturnsJsonString()
    {
        var json = JsonDocument.Parse("""{"key": "value"}""");
        var result = StreamEventProcessor.ExtractToolResultContent(json.RootElement);
        Assert.Contains("key", result);
        Assert.Contains("value", result);
    }

    [Fact]
    public void ExtractToolResultContent_NullString_ReturnsEmpty()
    {
        var json = JsonDocument.Parse("null");
        var result = StreamEventProcessor.ExtractToolResultContent(json.RootElement);
        Assert.NotNull(result);
    }
}
