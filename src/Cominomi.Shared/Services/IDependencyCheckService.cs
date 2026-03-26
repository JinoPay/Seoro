namespace Cominomi.Shared.Services;

public record InstallMethod(string Label, string InstallCommand, string UpdateCommand);

public record DependencyResult(
    string Name,
    string Description,
    bool IsInstalled,
    string? Version,
    string InstallUrl,
    string WindowsInstallHint,
    string MacInstallHint,
    IReadOnlyList<InstallMethod> WindowsMethods,
    IReadOnlyList<InstallMethod> MacMethods);

public interface IDependencyCheckService
{
    Task<List<DependencyResult>> CheckAllAsync();
}
