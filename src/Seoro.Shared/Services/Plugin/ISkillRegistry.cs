
namespace Seoro.Shared.Services.Plugin;

public interface ISkillRegistry
{
    bool TryParseSkillChain(string input, Session session, out List<SkillChainStep> steps);
    IReadOnlyList<SkillDefinition> GetAll();
    SkillDefinition? Find(string name);
    string ExpandSkill(SkillDefinition skill, string? args, Session session);
    string? TryParseSkillCommand(string input, out string? args);
    Task DeleteCommandAsync(string name, string scope, string? projectPath);
    Task LoadCustomCommandsAsync(string? projectPath);
    Task SaveCommandAsync(SkillDefinition command);
    void Register(SkillDefinition skill);
}