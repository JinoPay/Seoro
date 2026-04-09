namespace Seoro.Shared.Services.Platform;

public enum IdeLaunchMode
{
    Cli,
    MacApp
}

public record IdeInfo(string Name, string Command, string Icon, IdeLaunchMode LaunchMode = IdeLaunchMode.Cli);

public interface ILauncherService
{
    /// <summary>
    ///     폴더를 시스템 파일 탐색기에서 엽니다.
    /// </summary>
    Task OpenFolderAsync(string folderPath);

    /// <summary>
    ///     폴더를 지정된 IDE로 엽니다.
    /// </summary>
    Task OpenInIdeAsync(string folderPath, string ideCommand);

    /// <summary>
    ///     시스템에서 사용 가능한 IDE 목록을 반환합니다.
    /// </summary>
    Task<List<IdeInfo>> GetAvailableIdesAsync();
}