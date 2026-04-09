
namespace Seoro.Shared.Services.Chat;

public interface ISystemPromptBuilder
{
    Task<string?> BuildAsync(Session session, Workspace? workspace);
}