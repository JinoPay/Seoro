
namespace Seoro.Shared.Services.Knowledge;

public interface IInstructionsService
{
    string GetFilePath(ClaudeSettingsScope scope, string? projectPath = null);
    Task SaveAsync(ClaudeSettingsScope scope, string content, string? projectPath = null);
    Task<InstructionFile> ReadAsync(ClaudeSettingsScope scope, string? projectPath = null);
}