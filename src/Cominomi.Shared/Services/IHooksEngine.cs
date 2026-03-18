using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IHooksEngine
{
    Task<List<HookExecutionResult>> FireAsync(HookEvent hookEvent, Dictionary<string, string>? env = null);
    List<HookDefinition> GetHooks();
    Task AddHookAsync(HookDefinition hook);
    Task RemoveHookAsync(HookEvent hookEvent, string command);
    Task LoadAsync();
    Task SaveAsync();
}
