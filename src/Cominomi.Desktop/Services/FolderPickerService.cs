using System.Diagnostics;
using Cominomi.Shared.Services;

namespace Cominomi.Desktop.Services;

public class FolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return await PickFolderWindowsAsync();
        }
        else if (OperatingSystem.IsMacOS())
        {
            return await PickFolderMacAsync();
        }

        return null;
    }

    private static Task<string?> PickFolderWindowsAsync()
    {
        return Task.Run(() =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"" +
                    "Add-Type -AssemblyName System.Windows.Forms; " +
                    "$d = New-Object System.Windows.Forms.FolderBrowserDialog; " +
                    "if ($d.ShowDialog() -eq 'OK') { $d.SelectedPath } else { '' }\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();

            return string.IsNullOrEmpty(output) ? null : output;
        });
    }

    private static Task<string?> PickFolderMacAsync()
    {
        return Task.Run(() =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = "-e 'return POSIX path of (choose folder)'",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();

            return string.IsNullOrEmpty(output) || proc.ExitCode != 0 ? null : output;
        });
    }
}
