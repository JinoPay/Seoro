using Cominomi.Shared.Services;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

using AppUpdateInfo = Cominomi.Shared.Services.UpdateInfo;

namespace Cominomi.Desktop.Services;

public class UpdateService : IUpdateService
{
    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateManager _updateManager;
    private Velopack.UpdateInfo? _pendingUpdate;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        var token = Environment.GetEnvironmentVariable("COMINOMI_GITHUB_TOKEN");
        _updateManager = new UpdateManager(
            new GithubSource("https://github.com/JinoPay/Cominomi", token, false));
    }

    public bool IsInstalled => _updateManager.IsInstalled;

    public async Task<AppUpdateInfo?> CheckForUpdateAsync()
    {
        if (!IsInstalled)
        {
            _logger.LogDebug("Update check skipped: app not installed via Velopack");
            return null;
        }

        try
        {
            _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
            if (_pendingUpdate == null)
            {
                _logger.LogDebug("No updates available");
                return null;
            }

            var target = _pendingUpdate.TargetFullRelease;
            var version = target.Version.ToString();
            _logger.LogInformation("Update available: {Version}", version);
            return new AppUpdateInfo(version, target.Size);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            return null;
        }
    }

    public async Task DownloadUpdateAsync()
    {
        if (_pendingUpdate == null) return;

        try
        {
            _logger.LogInformation("Downloading update {Version}...", _pendingUpdate.TargetFullRelease.Version);
            await _updateManager.DownloadUpdatesAsync(_pendingUpdate);
            _logger.LogInformation("Update download complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update download failed");
            throw;
        }
    }

    public void ApplyUpdateAndRestart()
    {
        if (_pendingUpdate == null) return;

        _logger.LogInformation("Applying update and restarting...");
        _updateManager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
    }
}
