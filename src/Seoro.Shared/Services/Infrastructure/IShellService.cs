namespace Seoro.Shared.Services.Infrastructure;

public enum ShellType
{
    Bash,
    Cmd,
    Sh,
    Zsh,
    PowerShell
}

public record ShellInfo(string FileName, string ArgumentPrefix, ShellType Type);

public interface IShellService
{
    /// <summary>
    ///     Returns the list of shells available on this platform.
    ///     Only shells whose binaries exist on disk are returned.
    /// </summary>
    Task<List<ShellInfo>> GetAvailableShellsAsync();

    /// <summary>
    ///     Returns the resolved shell for internal commands (which, git, hooks, etc.).
    ///     On Windows, prefers Git Bash; falls back to cmd.exe.
    ///     On macOS/Linux, detects from $SHELL; falls back to /bin/zsh → /bin/bash → /bin/sh.
    ///     Result is cached with a TTL; call <see cref="InvalidateCache" /> to force re-resolution.
    /// </summary>
    Task<ShellInfo> GetShellAsync();

    /// <summary>
    ///     Returns the user's preferred terminal shell (from settings).
    ///     Falls back to the auto-detected default if no setting is configured.
    /// </summary>
    Task<ShellInfo> GetTerminalShellAsync();

    /// <summary>
    ///     Returns the user's full PATH from their login shell.
    ///     On macOS/Linux, runs a login shell (sourcing rc files) to capture PATH.
    ///     On Windows, returns the current process PATH.
    ///     Result is cached with ShellCacheTtl.
    /// </summary>
    Task<string?> GetLoginShellPathAsync();

    /// <summary>
    ///     Resolves an executable name to its full path.
    ///     Uses 'which' under bash/sh/zsh, 'where.exe' under cmd.
    /// </summary>
    Task<string?> WhichAsync(string executableName);

    /// <summary>
    ///     Clears the cached shell info so the next <see cref="GetShellAsync" /> re-detects.
    ///     Useful when dependencies are reinstalled after app start.
    /// </summary>
    void InvalidateCache();
}