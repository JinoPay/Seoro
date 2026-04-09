using System.Text;
using Seoro.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services;

public interface IAttachmentService
{
    string BuildMessageWithAttachments(string userText, List<FileAttachment> attachments);
    string GetAttachmentPath(string worktreePath, string storedFileName);
    Task<FileAttachment> CopyFileToWorktreeAsync(string sourceFilePath, string worktreePath);

    Task<FileAttachment>
        SaveBytesToWorktreeAsync(byte[] data, string fileName, string contentType, string worktreePath);
}

public class AttachmentService(ILogger<AttachmentService> logger) : IAttachmentService
{
    private const string AttachmentsDir = ".seoro-attachments";

    public string BuildMessageWithAttachments(string userText, List<FileAttachment> attachments)
    {
        if (attachments.Count == 0)
            return userText;

        var sb = new StringBuilder();
        foreach (var a in attachments)
        {
            var label = a.IsImage ? "Attached image" : "Attached file";
            sb.AppendLine($"[{label}: {AttachmentsDir}/{a.StoredFileName} (original: {a.OriginalFileName})]");
        }

        if (!string.IsNullOrWhiteSpace(userText))
        {
            sb.AppendLine();
            sb.Append(userText);
        }

        return sb.ToString();
    }

    public string GetAttachmentPath(string worktreePath, string storedFileName)
    {
        return Path.Combine(worktreePath, AttachmentsDir, storedFileName);
    }

    public async Task<FileAttachment> CopyFileToWorktreeAsync(string sourceFilePath, string worktreePath)
    {
        Guard.NotNullOrWhiteSpace(sourceFilePath, nameof(sourceFilePath));
        Guard.NotNullOrWhiteSpace(worktreePath, nameof(worktreePath));

        var dir = await EnsureAttachmentsDirAsync(worktreePath);
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

    public async Task<FileAttachment> SaveBytesToWorktreeAsync(byte[] data, string fileName, string contentType,
        string worktreePath)
    {
        Guard.NotNull(data, nameof(data));
        Guard.NotNullOrWhiteSpace(fileName, nameof(fileName));
        Guard.NotNullOrWhiteSpace(contentType, nameof(contentType));
        Guard.NotNullOrWhiteSpace(worktreePath, nameof(worktreePath));

        var dir = await EnsureAttachmentsDirAsync(worktreePath);
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
            await File.WriteAllBytesAsync(destPath, data);
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
            _ => "application/octet-stream"
        };
    }

    private static async Task EnsureGitignoreAsync(string worktreePath)
    {
        var gitignorePath = Path.Combine(worktreePath, ".gitignore");
        var entry = AttachmentsDir + "/";

        if (File.Exists(gitignorePath))
        {
            var lines = await File.ReadAllLinesAsync(gitignorePath);
            if (lines.Any(line => line.Trim() == entry))
                return;
            await AtomicFileWriter.AppendAsync(gitignorePath, $"\n{entry}\n");
        }
        else
        {
            await AtomicFileWriter.WriteAsync(gitignorePath, $"{entry}\n");
        }
    }

    private async Task<string> EnsureAttachmentsDirAsync(string worktreePath)
    {
        var dir = Path.Combine(worktreePath, AttachmentsDir);
        Directory.CreateDirectory(dir);
        await EnsureGitignoreAsync(worktreePath);
        return dir;
    }
}