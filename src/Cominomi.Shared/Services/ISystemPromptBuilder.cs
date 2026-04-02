using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISystemPromptBuilder
{
    Task<string?> BuildAsync(Session session, Workspace? workspace);
}