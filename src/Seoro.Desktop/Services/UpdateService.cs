using Seoro.Shared.Services;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

using AppUpdateInfo = Seoro.Shared.Services.Platform.UpdateInfo;

namespace Seoro.Desktop.Services;

public class UpdateService : IUpdateService
{
    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateManager _updateManager;
    private Velopack.UpdateInfo? _pendingUpdate;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        _updateManager = new UpdateManager(
            new GithubSource("https://github.com/JinoPay/Seoro", null, false));
    }

    public bool IsInstalled => _updateManager.IsInstalled;

    public async Task<AppUpdateInfo?> CheckForUpdateAsync()
    {
        if (!IsInstalled)
        {
            _logger.LogDebug("업데이트 확인 건너뜀: Velopack을 통해 설치된 앱이 아님");
            return null;
        }

        try
        {
            _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
            if (_pendingUpdate == null)
            {
                _logger.LogDebug("사용 가능한 업데이트 없음");
                return null;
            }

            var target = _pendingUpdate.TargetFullRelease;
            var version = target.Version.ToString();
            _logger.LogInformation("업데이트 사용 가능: {Version}", version);
            return new AppUpdateInfo(version, target.Size);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "업데이트 확인 실패");
            return null;
        }
    }

    public async Task DownloadUpdateAsync()
    {
        if (_pendingUpdate == null) return;

        try
        {
            _logger.LogInformation("업데이트 다운로드 중 {Version}...", _pendingUpdate.TargetFullRelease.Version);
            await _updateManager.DownloadUpdatesAsync(_pendingUpdate);
            _logger.LogInformation("업데이트 다운로드 완료");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "업데이트 다운로드 실패");
            throw;
        }
    }

    public void ApplyUpdateAndRestart()
    {
        if (_pendingUpdate == null) return;

        _logger.LogInformation("업데이트 적용 및 재시작 중...");
        _updateManager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
    }
}
