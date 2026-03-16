namespace Cominomi.Shared.Models;

public enum ContentGroupType
{
    Text,
    ToolGroup,
    FinalText
}

public class ContentGroup
{
    public ContentGroupType Type { get; set; }
    public List<ContentPart> Parts { get; set; } = [];
    public string Summary { get; set; } = "";
    public bool IsIntermediate { get; set; }
}
