namespace Cominomi.Shared.Services;

public record UpdateInfo(string TargetVersion, long? DownloadSize);

public interface IUpdateService
{
    bool IsInstalled { get; }
    Task DownloadUpdateAsync();
    Task<UpdateInfo?> CheckForUpdateAsync();
    void ApplyUpdateAndRestart();
}