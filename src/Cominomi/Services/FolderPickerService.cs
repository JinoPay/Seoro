using Cominomi.Shared.Services;

#if MACCATALYST
using UIKit;
using UniformTypeIdentifiers;
#elif WINDOWS
using Microsoft.UI.Xaml;
using WinRT.Interop;
#endif

namespace Cominomi.Services;

public class FolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync()
    {
#if MACCATALYST
        var tcs = new TaskCompletionSource<string?>();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var picker = new UIDocumentPickerViewController([UTTypes.Folder])
            {
                AllowsMultipleSelection = false
            };

            picker.DidPickDocumentAtUrls += (_, args) =>
            {
                var url = args.Urls?.FirstOrDefault();
                if (url != null)
                {
                    url.StartAccessingSecurityScopedResource();
                    tcs.TrySetResult(url.Path);
                    url.StopAccessingSecurityScopedResource();
                }
                else
                {
                    tcs.TrySetResult(null);
                }
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
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var window = (MauiWinUIWindow)Microsoft.Maui.Controls.Application.Current!.Windows[0].Handler.PlatformView!;
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
#else
        await Task.CompletedTask;
        return null;
#endif
    }
}
