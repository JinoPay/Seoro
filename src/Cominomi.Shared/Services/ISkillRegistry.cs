using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISkillRegistry
{
    IReadOnlyList<SkillDefinition> GetAll();
    SkillDefinition? Find(string name);
    string? TryParseSkillCommand(string input, out string? args);
    string ExpandSkill(SkillDefinition skill, string? args, Session session);
    bool TryParseSkillChain(string input, Session session, out List<SkillChainStep> steps);
    void Register(SkillDefinition skill);
    Task LoadCustomCommandsAsync(string? projectPath);
    Task SaveCommandAsync(SkillDefinition command);
    Task DeleteCommandAsync(string name, string scope, string? projectPath);
}
