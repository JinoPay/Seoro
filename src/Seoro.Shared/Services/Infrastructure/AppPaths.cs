namespace Seoro.Shared.Services.Infrastructure;

public static class AppPaths
{
    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Seoro");

    public static string ArchivedContexts { get; } = EnsureDir(Path.Combine(BaseDir, "archived-contexts"));
    public static string Memory { get; } = EnsureDir(Path.Combine(BaseDir, "memory"));
    public static string Repos { get; } = EnsureDir(Path.Combine(BaseDir, "repos"));

    public static string Sessions { get; } = EnsureDir(Path.Combine(BaseDir, "sessions"));
    public static string Settings { get; } = EnsureDir(BaseDir);
    public static string SettingsFile { get; } = Path.Combine(BaseDir, "settings.json");
    public static string SyncBackups { get; } = EnsureDir(Path.Combine(BaseDir, "sync-backups"));

    /// <summary>
    ///     스쿼시 머지 임시 클론 스테이징 디렉터리.
    ///     <c>SquashMergeViaTempCloneAsync</c>가 <c>git clone --no-hardlinks</c>로 만드는
    ///     일회성 작업 디렉터리의 부모 경로. 각 머지마다 하위에 GUID 디렉터리를 생성하고
    ///     작업이 끝나면(성공·실패 무관) 즉시 제거한다. 크래시로 고아가 된 디렉터리는
    ///     앱 다음 실행 시 <c>WorktreeSyncService.RecoverFromCrashAsync</c>와 동일한
    ///     패턴으로 정리할 수 있다.
    /// </summary>
    public static string MergeStaging { get; } = EnsureDir(Path.Combine(BaseDir, "merge-staging"));

    public static string Tasks { get; } = EnsureDir(Path.Combine(BaseDir, "tasks"));
    public static string Usage { get; } = EnsureDir(BaseDir);
    public static string Workspaces { get; } = EnsureDir(Path.Combine(BaseDir, "workspaces"));

    public static string AccountBackups { get; } = EnsureDir(Path.Combine(BaseDir, "account-backups"));
    public static string AccountsFile { get; } = Path.Combine(BaseDir, "accounts.json");

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}