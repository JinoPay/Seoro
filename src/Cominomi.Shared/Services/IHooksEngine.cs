using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IHooksEngine
{
    List<HookDefinition> GetHooks();
    Task AddHookAsync(HookDefinition hook);
    Task LoadAsync();
    Task RemoveHookAsync(HookEvent hookEvent, string command);
    Task SaveAsync();
    Task<List<HookExecutionResult>> FireAsync(HookEvent hookEvent, Dictionary<string, string>? env = null);
}