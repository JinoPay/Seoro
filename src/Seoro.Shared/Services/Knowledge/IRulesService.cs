
namespace Seoro.Shared.Services.Knowledge;

public interface IRulesService
{
    string GetRulesDirectory(ClaudeSettingsScope scope, string? projectPath = null);
    Task DeleteAsync(string filePath);
    Task SaveAsync(RuleFile rule);
    Task<List<RuleFile>> ListAsync(ClaudeSettingsScope scope, string? projectPath = null);
    Task<RuleFile?> ReadAsync(string filePath);
}