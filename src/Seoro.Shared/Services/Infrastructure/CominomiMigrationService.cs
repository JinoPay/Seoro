using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Infrastructure;

public record CominomiDataSummary(
    bool Exists,
    string BasePath,
    int SettingsFileCount,
    int SessionCount,
    int WorkspaceCount,
    int MemoryCount,
    int TaskCount,
    int RepoCount,
    int ArchivedContextCount)
{
    public int TotalFiles => SettingsFileCount + SessionCount + WorkspaceCount
                             + MemoryCount + TaskCount + RepoCount + ArchivedContextCount;
}

public record MigrationResult(int Copied, int Skipped, int Failed);

public class CominomiMigrationService(ILogger<CominomiMigrationService> logger)
{
    private static readonly string CominomiBaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Cominomi");

    private static readonly (string SubDir, string Label)[] Directories =
    [
        ("sessions", "sessions"),
        ("workspaces", "workspaces"),
        ("memory", "memory"),
        ("tasks", "tasks"),
        ("repos", "repos"),
        ("archived-contexts", "archived-contexts"),
    ];

    private static readonly string[] RootFiles = ["settings.json", "accounts.json"];

    public CominomiDataSummary GetDataSummary()
    {
        if (!Directory.Exists(CominomiBaseDir))
            return new CominomiDataSummary(false, CominomiBaseDir, 0, 0, 0, 0, 0, 0, 0);

        return new CominomiDataSummary(
            Exists: true,
            BasePath: CominomiBaseDir,
            SettingsFileCount: RootFiles.Count(f => File.Exists(Path.Combine(CominomiBaseDir, f))),
            SessionCount: CountFiles("sessions"),
            WorkspaceCount: CountFiles("workspaces"),
            MemoryCount: CountFiles("memory"),
            TaskCount: CountFiles("tasks"),
            RepoCount: CountFiles("repos"),
            ArchivedContextCount: CountFiles("archived-contexts"));
    }

    public async Task<MigrationResult> ImportAsync(bool overwrite)
    {
        int copied = 0, skipped = 0, failed = 0;

        // Copy root files (settings.json, accounts.json)
        foreach (var fileName in RootFiles)
        {
            var src = Path.Combine(CominomiBaseDir, fileName);
            if (!File.Exists(src)) continue;

            var dest = Path.Combine(AppPaths.Settings, fileName);
            if (File.Exists(dest) && !overwrite)
            {
                skipped++;
                continue;
            }

            try
            {
                var content = await File.ReadAllBytesAsync(src);
                await File.WriteAllBytesAsync(dest, content);
                copied++;
                logger.LogInformation("Migrated root file: {File}", fileName);

                // Reset onboarding fields so Seoro shows its own onboarding/WhatsNew
                if (fileName == "settings.json")
                    await ResetOnboardingFieldsAsync(dest);
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex, "루트 파일 마이그레이션 실패: {File}", fileName);
            }
        }

        // Copy directory contents
        foreach (var (subDir, _) in Directories)
        {
            var srcDir = Path.Combine(CominomiBaseDir, subDir);
            if (!Directory.Exists(srcDir)) continue;

            var destDir = subDir switch
            {
                "sessions" => AppPaths.Sessions,
                "workspaces" => AppPaths.Workspaces,
                "memory" => AppPaths.Memory,
                "tasks" => AppPaths.Tasks,
                "repos" => AppPaths.Repos,
                "archived-contexts" => AppPaths.ArchivedContexts,
                _ => Path.Combine(AppPaths.Settings, subDir)
            };

            foreach (var srcFile in Directory.GetFiles(srcDir, "*", SearchOption.TopDirectoryOnly))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(srcFile));
                if (File.Exists(destFile) && !overwrite)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var content = await File.ReadAllBytesAsync(srcFile);
                    await File.WriteAllBytesAsync(destFile, content);
                    copied++;
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogWarning(ex, "파일 마이그레이션 실패: {File}", srcFile);
                }
            }
        }

        logger.LogInformation("Cominomi 마이그레이션 완료: {Copied} 복사, {Skipped} 건너뜀, {Failed} 실패",
            copied, skipped, failed);

        return new MigrationResult(copied, skipped, failed);
    }

    public bool DeleteCominomiFolder()
    {
        if (!Directory.Exists(CominomiBaseDir))
            return true;

        try
        {
            Directory.Delete(CominomiBaseDir, recursive: true);
            logger.LogInformation("Deleted Cominomi data folder: {Path}", CominomiBaseDir);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cominomi 폴더 삭제 실패: {Path}", CominomiBaseDir);
            return false;
        }
    }

    private async Task ResetOnboardingFieldsAsync(string settingsPath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(settingsPath);
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj)
            {
                obj["OnboardingCompleted"] = false;
                obj["LastSeenVersion"] = "";
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(settingsPath, node.ToJsonString(options));
                logger.LogInformation("마이그레이션된 settings.json의 온보딩 필드 초기화됨");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "settings.json의 온보딩 필드 초기화 실패");
        }
    }

    private static int CountFiles(string subDir)
    {
        var dir = Path.Combine(CominomiBaseDir, subDir);
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly).Length
            : 0;
    }
}
