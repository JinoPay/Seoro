using System.Text;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IAttachmentService
{
    Task<FileAttachment> CopyFileToWorktreeAsync(string sourceFilePath, string worktreePath);
    Task<FileAttachment> SaveBytesToWorktreeAsync(byte[] data, string fileName, string contentType, string worktreePath);
    string GetAttachmentPath(string worktreePath, string storedFileName);
    string BuildMessageWithAttachments(string userText, List<FileAttachment> attachments);
}

public class AttachmentService : IAttachmentService
{
    private const string AttachmentsDir = ".cominomi-attachments";

    public async Task<FileAttachment> CopyFileToWorktreeAsync(string sourceFilePath, string worktreePath)
    {
        var dir = await EnsureAttachmentsDirAsync(worktreePath);
        var originalName = Path.GetFileName(sourceFilePath);
        var ext = Path.GetExtension(originalName);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        var destPath = Path.Combine(dir, storedName);

        await using var source = File.OpenRead(sourceFilePath);
        await using var dest = File.Create(destPath);
        await source.CopyToAsync(dest);

        var fileInfo = new FileInfo(destPath);
        return new FileAttachment
        {
            OriginalFileName = originalName,
            StoredFileName = storedName,
            ContentType = GetContentType(ext),
            SizeBytes = fileInfo.Length
        };
    }

    public async Task<FileAttachment> SaveBytesToWorktreeAsync(byte[] data, string fileName, string contentType, string worktreePath)
    {
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

        await File.WriteAllBytesAsync(destPath, data);

        return new FileAttachment
        {
            OriginalFileName = fileName,
            StoredFileName = storedName,
            ContentType = contentType,
            SizeBytes = data.Length
        };
    }

    public string GetAttachmentPath(string worktreePath, string storedFileName)
    {
        return Path.Combine(worktreePath, AttachmentsDir, storedFileName);
    }

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

    private async Task<string> EnsureAttachmentsDirAsync(string worktreePath)
    {
        var dir = Path.Combine(worktreePath, AttachmentsDir);
        Directory.CreateDirectory(dir);
        await EnsureGitignoreAsync(worktreePath);
        return dir;
    }

    private static async Task EnsureGitignoreAsync(string worktreePath)
    {
        var gitignorePath = Path.Combine(worktreePath, ".gitignore");
        var entry = AttachmentsDir + "/";

        if (File.Exists(gitignorePath))
        {
            var content = await File.ReadAllTextAsync(gitignorePath);
            if (content.Contains(entry))
                return;
            await AtomicFileWriter.AppendAsync(gitignorePath, $"\n{entry}\n");
        }
        else
        {
            await AtomicFileWriter.WriteAsync(gitignorePath, $"{entry}\n");
        }
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
}
