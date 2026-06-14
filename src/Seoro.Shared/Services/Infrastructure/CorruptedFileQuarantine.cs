using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Infrastructure;

/// <summary>
///     로드/역직렬화에 실패한 손상 파일을 침묵 삭제하거나 방치하지 않고
///     <see cref="AppPaths.Corrupted" /> 디렉터리로 옮겨 격리한다.
///     이렇게 하면 사용자는 데이터가 "사라진" 것이 아니라 격리되었음을 인지하고
///     필요 시 복구를 시도할 수 있으며, 손상된 파일이 다음 로드에서 반복 실패를 일으키지 않는다.
/// </summary>
public static class CorruptedFileQuarantine
{
    /// <summary>
    ///     손상된 파일을 격리 디렉터리로 이동한다. 모든 예외는 흡수한다(격리는 최선의 노력).
    /// </summary>
    /// <returns>이동된 격리 경로. 이동 실패 시 null.</returns>
    public static string? Quarantine(string filePath, ILogger logger, Exception? cause = null)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            // 타임스탬프(가독성) + 짧은 GUID(충돌 방지) + 원본 파일명.
            // 같은 밀리초에 동일 파일명이 두 번 격리돼도 서로 덮어쓰지 않는다.
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var unique = Guid.NewGuid().ToString("N")[..8];
            var destName = $"{stamp}_{unique}_{Path.GetFileName(filePath)}";
            var destPath = Path.Combine(AppPaths.Corrupted, destName);

            File.Move(filePath, destPath, true);
            logger.LogError(cause,
                "손상된 파일을 격리했습니다: {Source} → {Dest}", filePath, destPath);
            return destPath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "손상된 파일 격리 실패(원본 유지): {Source}", filePath);
            return null;
        }
    }
}
