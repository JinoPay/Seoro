using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IInstructionsService
{
    Task<InstructionFile> ReadAsync(ClaudeSettingsScope scope, string? projectPath = null);
    Task SaveAsync(ClaudeSettingsScope scope, string content, string? projectPath = null);
    string GetFilePath(ClaudeSettingsScope scope, string? projectPath = null);
}
