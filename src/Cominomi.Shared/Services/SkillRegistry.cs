using Cominomi.Shared;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SkillRegistry : ISkillRegistry
{
    private readonly List<SkillDefinition> _skills = [];
    private readonly SkillFileStore _fileStore;
    private readonly ILogger<SkillRegistry> _logger;

    public SkillRegistry(ILogger<SkillRegistry> logger)
    {
        _logger = logger;
        _fileStore = new SkillFileStore(logger);
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
        Guard.NotNull(skill, nameof(skill));
        Guard.NotNull(session, nameof(session));

        var template = skill.PromptTemplate;

        // Handle $ARGUMENTS placeholder (custom commands convention)
        if (template.Contains("$ARGUMENTS"))
            template = template.Replace("$ARGUMENTS", args ?? "");

        // Handle {args} placeholder (built-in convention)
        var expanded = template.Replace("{args}", args ?? "").Trim();
        return expanded;
    }

    public bool TryParseSkillChain(string input, Session session, out List<SkillChainStep> steps)
    {
        steps = [];
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith('/'))
            return false;

        // Split on " | " to get pipe-separated segments
        var segments = input.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            // Single command — check if skill itself has a chain defined
            var skillName = TryParseSkillCommand(input, out var skillArgs);
            if (skillName == null) return false;

            var skill = Find(skillName);
            if (skill == null || skill.Chain.Count == 0) return false;

            // First step: the command itself
            steps.Add(new SkillChainStep
            {
                SkillName = skillName,
                Args = skillArgs,
                ExpandedText = ExpandSkill(skill, skillArgs, session)
            });

            // Append chain from skill definition
            foreach (var chainedName in skill.Chain)
            {
                var chainedSkill = Find(chainedName);
                if (chainedSkill == null) continue;
                steps.Add(new SkillChainStep
                {
                    SkillName = chainedName,
                    ExpandedText = ExpandSkill(chainedSkill, null, session)
                });
            }

            return steps.Count > 1;
        }

        // Multiple pipe segments
        SkillDefinition? firstSkill = null;
        foreach (var segment in segments)
        {
            var seg = segment.Trim();
            if (!seg.StartsWith('/')) continue;

            var name = TryParseSkillCommand(seg, out var args);
            if (name == null) continue;

            var skill = Find(name);
            if (skill == null) continue;

            firstSkill ??= skill;
            steps.Add(new SkillChainStep
            {
                SkillName = name,
                Args = args,
                ExpandedText = ExpandSkill(skill, args, session)
            });
        }

        // Append chain from first skill's definition (after explicit pipe steps)
        if (firstSkill?.Chain.Count > 0)
        {
            foreach (var chainedName in firstSkill.Chain)
            {
                if (steps.Any(s => s.SkillName.Equals(chainedName, StringComparison.OrdinalIgnoreCase)))
                    continue; // skip duplicates
                var chainedSkill = Find(chainedName);
                if (chainedSkill == null) continue;
                steps.Add(new SkillChainStep
                {
                    SkillName = chainedName,
                    ExpandedText = ExpandSkill(chainedSkill, null, session)
                });
            }
        }

        return steps.Count > 1;
    }

    public void Register(SkillDefinition skill)
    {
        var existing = _skills.FindIndex(s => s.Name == skill.Name);
        if (existing >= 0)
            _skills[existing] = skill;
        else
            _skills.Add(skill);
    }

    public async Task LoadCustomCommandsAsync(string? projectPath)
    {
        // Remove previously loaded custom commands
        _skills.RemoveAll(s => !s.IsBuiltIn);

        // User-scope commands: ~/.claude/commands/
        var userDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "commands");
        var userSkills = await _fileStore.LoadFromDirectoryAsync(userDir, "user");
        _skills.AddRange(userSkills);

        // Project-scope commands: <project>/.claude/commands/
        if (!string.IsNullOrEmpty(projectPath))
        {
            var projectDir = Path.Combine(projectPath, ".claude", "commands");
            var projectSkills = await _fileStore.LoadFromDirectoryAsync(projectDir, "project");
            _skills.AddRange(projectSkills);
        }
    }

    public async Task SaveCommandAsync(SkillDefinition command)
    {
        await _fileStore.SaveAsync(command);
        Register(command);
    }

    public Task DeleteCommandAsync(string name, string scope, string? projectPath)
    {
        var skill = _skills.FirstOrDefault(s => s.Name == name && s.Scope == scope);
        if (skill != null)
        {
            _fileStore.Delete(skill);
        }

        _skills.RemoveAll(s => s.Name == name && s.Scope == scope);
        return Task.CompletedTask;
    }
}
