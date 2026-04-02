namespace Cominomi.Shared.Models;

public class FileAttachment
{
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
}

public class ChatInputMessage
{
    public List<PendingAttachment> Attachments { get; set; } = [];
    public List<SkillChainStep>? PendingChain { get; set; }
    public string Text { get; set; } = string.Empty;
}

public class PendingAttachment
{
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    public byte[] Data { get; set; } = [];
    public string ContentType { get; set; } = "application/octet-stream";
    public string FileName { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string? PreviewDataUrl { get; set; }
}