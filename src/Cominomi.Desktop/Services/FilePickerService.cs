using System.Diagnostics;
using Cominomi.Shared.Models;
using Cominomi.Shared.Services;

namespace Cominomi.Desktop.Services;

public class FilePickerService : IFilePickerService
{
    public async Task<List<PendingAttachment>?> PickFilesAsync()
    {
        string[]? filePaths;

        if (OperatingSystem.IsWindows())
        {
            filePaths = await PickFilesWindowsAsync();
        }
        else if (OperatingSystem.IsMacOS())
        {
            filePaths = await PickFilesMacAsync();
        }
        else
        {
            return null;
        }

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
            {
                attachment.PreviewDataUrl = $"data:{contentType};base64,{Convert.ToBase64String(data)}";
            }

            results.Add(attachment);
        }

        return results.Count > 0 ? results : null;
    }

    private static Task<string[]?> PickFilesWindowsAsync()
    {
        return Task.Run(() =>
        {
            // Use PowerShell to show a native file dialog
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"" +
                    "Add-Type -AssemblyName System.Windows.Forms; " +
                    "$d = New-Object System.Windows.Forms.OpenFileDialog; " +
                    "$d.Multiselect = $true; " +
                    "$d.Filter = 'All files (*.*)|*.*'; " +
                    "if ($d.ShowDialog() -eq 'OK') { $d.FileNames -join '|' } else { '' }\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();

            if (string.IsNullOrEmpty(output)) return null;
            return output.Split('|', StringSplitOptions.RemoveEmptyEntries);
        });
    }

    private static Task<string[]?> PickFilesMacAsync()
    {
        return Task.Run(() =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = "-e 'set theFiles to choose file with multiple selections allowed' " +
                            "-e 'set output to \"\"' " +
                            "-e 'repeat with f in theFiles' " +
                            "-e 'set output to output & POSIX path of f & \"|\"' " +
                            "-e 'end repeat' " +
                            "-e 'return output'",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();

            if (string.IsNullOrEmpty(output) || proc.ExitCode != 0) return null;
            return output.Split('|', StringSplitOptions.RemoveEmptyEntries);
        });
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
