namespace Cominomi.Shared.Services;

public enum ShellType { Bash, Cmd, Sh }

public record ShellInfo(string FileName, string ArgumentPrefix, ShellType Type);

public interface IShellService
{
    /// <summary>
    /// Returns the resolved shell for the current platform.
    /// On Windows, prefers Git Bash; falls back to cmd.exe.
    /// On macOS/Linux, returns /bin/sh.
    /// </summary>
    Task<ShellInfo> GetShellAsync();

    /// <summary>
    /// Resolves an executable name to its full path.
    /// Uses 'which' under bash/sh, 'where.exe' under cmd.
    /// </summary>
    Task<string?> WhichAsync(string executableName);
}
