namespace Cominomi.Shared.Services;

public record UpdateInfo(string TargetVersion, long? DownloadSize);

public interface IUpdateService
{
    Task<UpdateInfo?> CheckForUpdateAsync();
    Task DownloadUpdateAsync();
    void ApplyUpdateAndRestart();
    bool IsInstalled { get; }
}
