namespace Cominomi.Shared.Services;

public record DependencyResult(
    string Name,
    string Description,
    bool IsInstalled,
    string? Version,
    string InstallUrl,
    string WindowsInstallHint,
    string MacInstallHint);

public interface IDependencyCheckService
{
    Task<List<DependencyResult>> CheckAllAsync();
}
