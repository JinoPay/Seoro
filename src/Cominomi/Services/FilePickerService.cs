using Cominomi.Shared.Models;
using Cominomi.Shared.Services;

#if MACCATALYST
using UIKit;
using UniformTypeIdentifiers;
#elif WINDOWS
using Microsoft.UI.Xaml;
using WinRT.Interop;
#endif

namespace Cominomi.Services;

public class FilePickerService : IFilePickerService
{
    public async Task<List<PendingAttachment>?> PickFilesAsync()
    {
#if MACCATALYST
        var tcs = new TaskCompletionSource<List<PendingAttachment>?>();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var allowedTypes = new[] { UTTypes.Content, UTTypes.Image };
            var picker = new UIDocumentPickerViewController(allowedTypes)
            {
                AllowsMultipleSelection = true,
                ShouldShowFileExtensions = true
            };

            picker.DidPickDocumentAtUrls += (_, args) =>
            {
                var results = new List<PendingAttachment>();
                if (args.Urls != null)
                {
                    foreach (var url in args.Urls)
                    {
                        url.StartAccessingSecurityScopedResource();
                        try
                        {
                            var path = url.Path;
                            if (path != null && File.Exists(path))
                            {
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
                        }
                        finally
                        {
                            url.StopAccessingSecurityScopedResource();
                        }
                    }
                }
                tcs.TrySetResult(results.Count > 0 ? results : null);
            };

            picker.WasCancelled += (_, _) =>
            {
                tcs.TrySetResult(null);
            };

            var viewController = Platform.GetCurrentUIViewController();
            viewController?.PresentViewController(picker, true, null);
        });

        return await tcs.Task;
#elif WINDOWS
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var window = (MauiWinUIWindow)Microsoft.Maui.Controls.Application.Current!.Windows[0].Handler.PlatformView!;
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var files = await picker.PickMultipleFilesAsync();
        if (files == null || files.Count == 0)
            return null;

        var results = new List<PendingAttachment>();
        foreach (var file in files)
        {
            var path = file.Path;
            if (File.Exists(path))
            {
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
        }
        return results.Count > 0 ? results : null;
#else
        await Task.CompletedTask;
        return null;
#endif
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
