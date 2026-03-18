using Cominomi.Shared.Models;
using Cominomi.Shared.Services;

namespace Cominomi.Shared.Tests;

public class ToolDisplayHelperTests
{
    [Theory]
    [InlineData("Read", """{"file_path": "/src/app/Component.razor"}""", "Read app/Component.razor")]
    [InlineData("Write", """{"file_path": "/src/services/MyService.cs"}""", "Write services/MyService.cs")]
    [InlineData("Edit", """{"file_path": "/src/Models/User.cs"}""", "Edit Models/User.cs")]
    [InlineData("Bash", """{"command": "dotnet build"}""", "Bash dotnet build")]
    [InlineData("Grep", """{"pattern": "TODO"}""", "Grep TODO")]
    [InlineData("Glob", """{"pattern": "**/*.cs"}""", "Glob **/*.cs")]
    public void GetHeaderLabel_ReturnsContextualLabel(string name, string input, string expected)
    {
        var tool = new ToolCall { Name = name, Input = input };
        var result = ToolDisplayHelper.GetHeaderLabel(tool);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetHeaderLabel_NoInput_ReturnsNameOnly()
    {
        var tool = new ToolCall { Name = "Read", Input = "" };
        var result = ToolDisplayHelper.GetHeaderLabel(tool);
        Assert.Equal("Read", result);
    }

    [Fact]
    public void GetHeaderLabel_NormalizesToolNames()
    {
        var tool = new ToolCall { Name = "read_file", Input = """{"file_path": "/a/b.cs"}""" };
        var result = ToolDisplayHelper.GetHeaderLabel(tool);
        Assert.StartsWith("Read", result);
    }

    [Fact]
    public void GetHeaderLabel_AgentWithDescription()
    {
        var tool = new ToolCall
        {
            Name = "Agent",
            Input = """{"description": "Search for tests"}"""
        };
        var result = ToolDisplayHelper.GetHeaderLabel(tool);
        Assert.Equal("Agent Search for tests", result);
    }

    [Fact]
    public void GetHeaderLabel_McpTool_ExtractsName()
    {
        var tool = new ToolCall { Name = "mcp__server__get_data", Input = "" };
        var result = ToolDisplayHelper.GetHeaderLabel(tool);
        Assert.Equal("get_data", result);
    }

    [Fact]
    public void GetCompactResult_ReadOutput_ShowsLineCount()
    {
        var tool = new ToolCall
        {
            Name = "Read",
            Output = "line1\nline2\nline3\n",
            IsComplete = true
        };
        var result = ToolDisplayHelper.GetCompactResult(tool);
        Assert.Equal("3줄 읽음", result);
    }

    [Fact]
    public void GetCompactResult_IncompleteTask_ReturnsNull()
    {
        var tool = new ToolCall { Name = "Read", Output = "data", IsComplete = false };
        Assert.Null(ToolDisplayHelper.GetCompactResult(tool));
    }

    [Fact]
    public void GetCompactResult_EmptyOutput_ReturnsNull()
    {
        var tool = new ToolCall { Name = "Read", Output = "", IsComplete = true };
        Assert.Null(ToolDisplayHelper.GetCompactResult(tool));
    }

    [Fact]
    public void BuildDescriptiveSummary_SingleRead()
    {
        var parts = new List<ContentPart>
        {
            new() { Type = ContentPartType.ToolCall, ToolCall = new ToolCall { Name = "Read" } }
        };
        var result = ToolDisplayHelper.BuildDescriptiveSummary(parts);
        Assert.Equal("1개 파일 읽음", result);
    }

    [Fact]
    public void BuildDescriptiveSummary_MultipleTools()
    {
        var parts = new List<ContentPart>
        {
            new() { Type = ContentPartType.ToolCall, ToolCall = new ToolCall { Name = "Read" } },
            new() { Type = ContentPartType.ToolCall, ToolCall = new ToolCall { Name = "Read" } },
            new() { Type = ContentPartType.ToolCall, ToolCall = new ToolCall { Name = "Edit" } }
        };
        var result = ToolDisplayHelper.BuildDescriptiveSummary(parts);
        Assert.Contains("2개 파일 읽음", result);
        Assert.Contains("1개 파일 수정됨", result);
    }

    [Fact]
    public void BuildDescriptiveSummary_BashMultiple()
    {
        var parts = new List<ContentPart>
        {
            new() { Type = ContentPartType.ToolCall, ToolCall = new ToolCall { Name = "Bash" } },
            new() { Type = ContentPartType.ToolCall, ToolCall = new ToolCall { Name = "Bash" } },
            new() { Type = ContentPartType.ToolCall, ToolCall = new ToolCall { Name = "Bash" } }
        };
        var result = ToolDisplayHelper.BuildDescriptiveSummary(parts);
        Assert.Equal("명령 3회 실행됨", result);
    }

    [Fact]
    public void GetHeaderLabel_LongCommand_Truncated()
    {
        var longCmd = new string('x', 100);
        var tool = new ToolCall { Name = "Bash", Input = $$$"""{"command": "{{{longCmd}}}"}""" };
        var result = ToolDisplayHelper.GetHeaderLabel(tool);
        Assert.Contains("…", result);
    }

    [Fact]
    public void GetHeaderLabel_ShortFilePath_ShowsFull()
    {
        var tool = new ToolCall { Name = "Read", Input = """{"file_path": "file.cs"}""" };
        var result = ToolDisplayHelper.GetHeaderLabel(tool);
        Assert.Equal("Read file.cs", result);
    }
}
