namespace Cominomi.Shared.Models.ViewModels;

public enum ContentGroupType
{
    Text,
    ToolGroup,
    FinalText,
    Thinking
}

public class ContentGroup
{
    public bool IsIntermediate { get; set; }
    public ContentGroupType Type { get; set; }
    public List<ContentPart> Parts { get; set; } = [];
    public string Summary { get; set; } = "";
}