using System.Text;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Infrastructure;

public interface IAttachmentService
{
    string BuildMessageWithAttachments(string userText, List<FileAttachment> attachments, string workspaceId,
        string sessionId);

    string GetAttachmentPath(string workspaceId, string sessionId, string storedFileName);
    Task<FileAttachment> CopyFileToSessionAsync(string sourceFilePath, string workspaceId, string sessionId);

    Task<FileAttachment> SaveBytesToSessionAsync(byte[] data, string fileName, string contentType,
        string workspaceId, string sessionId);
}

public class AttachmentService(ILogger<AttachmentService> logger) : IAttachmentService
{
    public string BuildMessageWithAttachments(string userText, List<FileAttachment> attachments, string workspaceId,
        string sessionId)
    {
        if (attachments.Count == 0)
            return userText;

        var sb = new StringBuilder();
        foreach (var a in attachments)
        {
            var label = a.IsImage ? "Attached image" : "Attached file";
            // 앱데이터(워크트리 밖)에 저장되므로 Claude가 cwd 무관하게 읽도록 절대경로를 사용한다.
            var absolutePath = GetAttachmentPath(workspaceId, sessionId, a.StoredFileName);
            sb.AppendLine($"[{label}: {absolutePath} (original: {a.OriginalFileName})]");
        }

        if (!string.IsNullOrWhiteSpace(userText))
        {
            sb.AppendLine();
            sb.Append(userText);
        }

        return sb.ToString();
    }

    public string GetAttachmentPath(string workspaceId, string sessionId, string storedFileName)
    {
        return Path.Combine(AppPaths.AttachmentsForSession(workspaceId, sessionId), storedFileName);
    }

    public async Task<FileAttachment> CopyFileToSessionAsync(string sourceFilePath, string workspaceId,
        string sessionId)
    {
        Guard.NotNullOrWhiteSpace(sourceFilePath, nameof(sourceFilePath));
        Guard.NotNullOrWhiteSpace(workspaceId, nameof(workspaceId));
        Guard.NotNullOrWhiteSpace(sessionId, nameof(sessionId));

        var dir = EnsureSessionDir(workspaceId, sessionId);
        var originalName = Path.GetFileName(sourceFilePath);
        var ext = Path.GetExtension(originalName);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        var destPath = Path.Combine(dir, storedName);

        try
        {
            await using var source = File.OpenRead(sourceFilePath);
            await using var dest = File.Create(destPath);
            await source.CopyToAsync(dest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "첨부파일 복사 실패: {SourcePath}", sourceFilePath);
            throw;
        }

        var fileInfo = new FileInfo(destPath);
        logger.LogDebug("첨부파일 복사됨: {OriginalName} -> {StoredName} ({SizeBytes} 바이트)", originalName,
            storedName, fileInfo.Length);
        return new FileAttachment
        {
            OriginalFileName = originalName,
            StoredFileName = storedName,
            ContentType = GetContentType(ext),
            SizeBytes = fileInfo.Length
        };
    }

    public async Task<FileAttachment> SaveBytesToSessionAsync(byte[] data, string fileName, string contentType,
        string workspaceId, string sessionId)
    {
        Guard.NotNull(data, nameof(data));
        Guard.NotNullOrWhiteSpace(fileName, nameof(fileName));
        Guard.NotNullOrWhiteSpace(contentType, nameof(contentType));
        Guard.NotNullOrWhiteSpace(workspaceId, nameof(workspaceId));
        Guard.NotNullOrWhiteSpace(sessionId, nameof(sessionId));

        var dir = EnsureSessionDir(workspaceId, sessionId);
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) && contentType.StartsWith("image/"))
        {
            ext = contentType switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "image/svg+xml" => ".svg",
                _ => ".png"
            };
            if (!fileName.Contains('.'))
                fileName += ext;
        }

        var storedName = $"{Guid.NewGuid():N}{ext}";
        var destPath = Path.Combine(dir, storedName);

        try
        {
            await AtomicFileWriter.WriteAsync(destPath, data);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "첨부파일 저장 실패: {FileName}", fileName);
            throw;
        }

        logger.LogDebug("첨부파일 저장됨: {FileName} ({Size} 바이트)", fileName, data.Length);
        return new FileAttachment
        {
            OriginalFileName = fileName,
            StoredFileName = storedName,
            ContentType = contentType,
            SizeBytes = data.Length
        };
    }

    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".md" => "text/markdown",
            ".yaml" or ".yml" => "text/yaml",
            _ => "application/octet-stream"
        };
    }

    private static string EnsureSessionDir(string workspaceId, string sessionId)
    {
        var dir = AppPaths.AttachmentsForSession(workspaceId, sessionId);
        Directory.CreateDirectory(dir);
        return dir;
    }
}