namespace Cominomi.Shared.Models;

public class FileAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

public class ChatInputMessage
{
    public string Text { get; set; } = string.Empty;
    public List<PendingAttachment> Attachments { get; set; } = [];
}

public class PendingAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Data { get; set; } = [];
    public string? FilePath { get; set; }
    public string? PreviewDataUrl { get; set; }
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
