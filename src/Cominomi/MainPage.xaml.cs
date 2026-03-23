#if WINDOWS
using Microsoft.Web.WebView2.Core;
#endif
using Serilog;

namespace Cominomi;

public partial class MainPage
{
    public MainPage()
    {
        InitializeComponent();

        blazorWebView.BlazorWebViewInitialized += (_, args) =>
        {
#if WINDOWS
            args.WebView.CoreWebView2.ProcessFailed += (_, e) =>
            {
                Log.Error("WebView2 process failed: Kind={Kind}, Reason={Reason}",
                    e.ProcessFailedKind, e.Reason);
            };

            // Auto-approve clipboard read permission so paste (Ctrl+V) works for images/files
            args.WebView.CoreWebView2.PermissionRequested += (_, permArgs) =>
            {
                if (permArgs.PermissionKind == CoreWebView2PermissionKind.ClipboardRead)
                    permArgs.State = CoreWebView2PermissionState.Allow;
            };
#endif
        };
    }
}