using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class SkillRegistry : ISkillRegistry
{
    private readonly List<SkillDefinition> _skills = [];

    public SkillRegistry()
    {
        RegisterBuiltIns();
    }

    private void RegisterBuiltIns()
    {
        _skills.AddRange([
            new SkillDefinition
            {
                Name = "commit",
                Description = "Commit current changes with a descriptive message",
                PromptTemplate = "Review all current changes with `git diff` and `git status`, then create a git commit with a clear, descriptive commit message that explains what changed and why. {args}",
                IsBuiltIn = true
            },
            new SkillDefinition
            {
                Name = "review",
                Description = "Review code changes and provide feedback",
                PromptTemplate = "Review the current code changes (`git diff`) and provide detailed feedback on code quality, potential bugs, and improvements. {args}",
                IsBuiltIn = true
            },
            new SkillDefinition
            {
                Name = "simplify",
                Description = "Review changed code for reuse, quality, and efficiency",
                PromptTemplate = "Review the recently changed code for opportunities to simplify, improve reuse, and increase efficiency. Fix any issues found. {args}",
                IsBuiltIn = true
            },
            new SkillDefinition
            {
                Name = "test",
                Description = "Run tests and report results",
                PromptTemplate = "Find and run the project's test suite. Report any failures with details. {args}",
                IsBuiltIn = true
            },
            new SkillDefinition
            {
                Name = "explain",
                Description = "Explain how the codebase works",
                PromptTemplate = "Explain how the codebase or the specified part works in detail. {args}",
                IsBuiltIn = true
            },
            new SkillDefinition
            {
                Name = "fix",
                Description = "Fix a bug or issue",
                PromptTemplate = "Investigate and fix the following issue: {args}",
                IsBuiltIn = true
            },
            new SkillDefinition
            {
                Name = "plan",
                Description = "Create a detailed implementation plan",
                PromptTemplate = "Create a detailed implementation plan for the following task. Include file paths, code changes, and verification steps. Save the plan to .context/plans/. Task: {args}",
                IsBuiltIn = true
            },
            new SkillDefinition
            {
                Name = "compact",
                Description = "Compact conversation to free context space",
                PromptTemplate = "Summarize the conversation so far into a compact context, focusing on key decisions, code changes made, and current state. Discard verbose tool outputs and intermediate reasoning. {args}",
                IsBuiltIn = true
            },
            new SkillDefinition
            {
                Name = "security-review",
                Description = "Analyze changes for security vulnerabilities",
                PromptTemplate = "Analyze the pending changes on the current branch for security vulnerabilities. Review the git diff and identify risks like injection, auth issues, data exposure, and other OWASP top 10 concerns. {args}",
                IsBuiltIn = true
            },
            new SkillDefinition
            {
                Name = "pr-comments",
                Description = "Fetch and address PR comments",
                PromptTemplate = "Fetch the comments from the current pull request using `gh pr view --comments` and address any feedback or requested changes. {args}",
                IsBuiltIn = true
            },
            new SkillDefinition
            {
                Name = "debug",
                Description = "Debug and diagnose an issue",
                PromptTemplate = "Debug and diagnose the following issue. Check logs, error messages, and code paths to find the root cause and suggest a fix: {args}",
                IsBuiltIn = true
            }
        ]);
    }

    public IReadOnlyList<SkillDefinition> GetAll() => _skills.AsReadOnly();

    public SkillDefinition? Find(string name)
        => _skills.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public string? TryParseSkillCommand(string input, out string? args)
    {
        args = null;
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith('/'))
            return null;

        var trimmed = input.TrimStart('/');
        var spaceIdx = trimmed.IndexOf(' ');

        string name;
        if (spaceIdx > 0)
        {
            name = trimmed[..spaceIdx];
            args = trimmed[(spaceIdx + 1)..].Trim();
        }
        else
        {
            name = trimmed;
        }

        var skill = Find(name);
        return skill?.Name;
    }

    public string ExpandSkill(SkillDefinition skill, string? args, Session session)
    {
        var expanded = skill.PromptTemplate.Replace("{args}", args ?? "").Trim();
        return expanded;
    }

    public void Register(SkillDefinition skill)
    {
        var existing = _skills.FindIndex(s => s.Name == skill.Name);
        if (existing >= 0)
            _skills[existing] = skill;
        else
            _skills.Add(skill);
    }
}
