using Cominomi.Shared.Models;
using Cominomi.Shared.Services;

namespace Cominomi.Desktop.Services;

public class FilePickerService(PhotinoWindowHolder windowHolder) : IFilePickerService
{
    public async Task<List<PendingAttachment>?> PickFilesAsync()
    {
        var window = windowHolder.Window;
        if (window == null) return null;

        var filePaths = await window.ShowOpenFileAsync(
            "파일 선택",
            multiSelect: true);

        if (filePaths == null || filePaths.Length == 0)
            return null;

        var results = new List<PendingAttachment>();
        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;

            var fileName = Path.GetFileName(path);
            var data = File.ReadAllBytes(path);
            var contentType = GetContentType(Path.GetExtension(fileName));

            var attachment = new PendingAttachment
            {
                FileName = fileName,
                ContentType = contentType,
                Data = data,
                FilePath = path
            };

            if (attachment.IsImage)
                attachment.PreviewDataUrl = $"data:{contentType};base64,{Convert.ToBase64String(data)}";

            results.Add(attachment);
        }

        return results.Count > 0 ? results : null;
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