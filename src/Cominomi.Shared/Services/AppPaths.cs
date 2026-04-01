namespace Cominomi.Shared.Services;

public static class AppPaths
{
    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Cominomi");

    public static string Sessions { get; } = EnsureDir(Path.Combine(BaseDir, "sessions"));
    public static string ArchivedContexts { get; } = EnsureDir(Path.Combine(BaseDir, "archived-contexts"));
    public static string Workspaces { get; } = EnsureDir(Path.Combine(BaseDir, "workspaces"));
    public static string Repos { get; } = EnsureDir(Path.Combine(BaseDir, "repos"));
    public static string Memory { get; } = EnsureDir(Path.Combine(BaseDir, "memory"));
    public static string Tasks { get; } = EnsureDir(Path.Combine(BaseDir, "tasks"));
    public static string SyncBackups { get; } = EnsureDir(Path.Combine(BaseDir, "sync-backups"));
    public static string Usage { get; } = EnsureDir(BaseDir);
    public static string Settings { get; } = EnsureDir(BaseDir);
    public static string SettingsFile { get; } = Path.Combine(BaseDir, "settings.json");

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
