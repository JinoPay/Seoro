using Seoro.Shared.Services.Migration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Seoro.Shared.Services.Settings;

/// <summary>
///     Loads AppSettings from the JSON settings file each time IOptionsMonitor needs a fresh instance.
/// </summary>
public class AppSettingsFactory(ILogger<AppSettingsFactory> logger) : IOptionsFactory<AppSettings>
{
    public AppSettings Create(string name)
    {
        var path = AppPaths.SettingsFile;
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            var (settings, migrated, migratedJson) = MigratingJsonReader.Read<AppSettings>(json, JsonDefaults.Options);
            var result = settings ?? new AppSettings();
            result.DefaultModel = ModelDefinitions.NormalizeModelId(result.DefaultModel);
            if (migrated && migratedJson != null)
                // Create()는 동기 인터페이스(IOptionsFactory)라 async화할 수 없다.
                // Task.Run 으로 호출자의 SynchronizationContext(UI 스레드 등)와 분리해
                // 동기 GetResult() 가 UI 컨텍스트에서 데드락나는 것을 방지한다.
                Task.Run(() => AtomicFileWriter.WriteAsync(path, migratedJson)).GetAwaiter().GetResult();
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Path}에서 설정 로드 실패, 기본값 사용", path);
            return new AppSettings();
        }
    }
}