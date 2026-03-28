using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IRulesService
{
    Task<List<RuleFile>> ListAsync(ClaudeSettingsScope scope, string? projectPath = null);
    Task<RuleFile?> ReadAsync(string filePath);
    Task SaveAsync(RuleFile rule);
    Task DeleteAsync(string filePath);
    string GetRulesDirectory(ClaudeSettingsScope scope, string? projectPath = null);
}
