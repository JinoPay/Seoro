using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SkillRegistry : ISkillRegistry
{
    private readonly List<SkillDefinition> _skills = [];
    private readonly ILogger<SkillRegistry> _logger;

    public SkillRegistry(ILogger<SkillRegistry> logger)
    {
        _logger = logger;
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
        var template = skill.PromptTemplate;

        // Handle $ARGUMENTS placeholder (custom commands convention)
        if (template.Contains("$ARGUMENTS"))
            template = template.Replace("$ARGUMENTS", args ?? "");

        // Handle {args} placeholder (built-in convention)
        var expanded = template.Replace("{args}", args ?? "").Trim();
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

    public async Task LoadCustomCommandsAsync(string? projectPath)
    {
        // Remove previously loaded custom commands
        _skills.RemoveAll(s => !s.IsBuiltIn);

        // User-scope commands: ~/.claude/commands/
        var userDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "commands");
        await LoadCommandsFromDirectoryAsync(userDir, "user");

        // Project-scope commands: <project>/.claude/commands/
        if (!string.IsNullOrEmpty(projectPath))
        {
            var projectDir = Path.Combine(projectPath, ".claude", "commands");
            await LoadCommandsFromDirectoryAsync(projectDir, "project");
        }
    }

    private async Task LoadCommandsFromDirectoryAsync(string directory, string scope)
    {
        if (!Directory.Exists(directory))
            return;

        try
        {
            var files = Directory.GetFiles(directory, "*.md", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    var skill = ParseCommandFile(file, content, scope, directory);
                    if (skill != null)
                        _skills.Add(skill);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse command file: {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scan command directory: {Dir}", directory);
        }
    }

    private static SkillDefinition? ParseCommandFile(string filePath, string content, string scope, string baseDir)
    {
        var relativePath = Path.GetRelativePath(baseDir, filePath);
        var nameWithoutExt = Path.ChangeExtension(relativePath, null).Replace('\\', '/');

        // Determine namespace from subdirectory
        string? ns = null;
        var name = nameWithoutExt;
        var slashIdx = nameWithoutExt.LastIndexOf('/');
        if (slashIdx >= 0)
        {
            ns = nameWithoutExt[..slashIdx].Replace('/', ':');
            name = nameWithoutExt;
        }

        // Parse optional YAML frontmatter
        string description = "";
        var allowedTools = new List<string>();
        var body = content;

        var lines = content.Split('\n');
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var endIdx = -1;
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---")
                {
                    endIdx = i;
                    break;
                }
            }

            if (endIdx > 0)
            {
                // Simple YAML parsing for description and allowed-tools
                for (int i = 1; i < endIdx; i++)
                {
                    var line = lines[i].Trim();
                    if (line.StartsWith("description:"))
                        description = line["description:".Length..].Trim().Trim('"', '\'');
                    else if (line.StartsWith("allowed-tools:"))
                    {
                        var toolsStr = line["allowed-tools:".Length..].Trim();
                        if (toolsStr.StartsWith('['))
                        {
                            toolsStr = toolsStr.Trim('[', ']');
                            allowedTools = toolsStr.Split(',').Select(t => t.Trim().Trim('"', '\'')).Where(t => !string.IsNullOrEmpty(t)).ToList();
                        }
                    }
                    else if (line.StartsWith("- ") && allowedTools.Count > 0)
                    {
                        allowedTools.Add(line[2..].Trim());
                    }
                }

                body = string.Join('\n', lines[(endIdx + 1)..]).TrimStart();
            }
        }

        var acceptsArguments = body.Contains("$ARGUMENTS");

        return new SkillDefinition
        {
            Name = name.Replace('/', ':'),
            Description = !string.IsNullOrEmpty(description) ? description : $"Custom command: {name}",
            PromptTemplate = body,
            IsBuiltIn = false,
            Scope = scope,
            AllowedTools = allowedTools,
            Namespace = ns,
            AcceptsArguments = acceptsArguments,
            FilePath = filePath
        };
    }

    public async Task SaveCommandAsync(SkillDefinition command)
    {
        var dir = command.Scope == "project" ? null : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "commands");

        if (dir == null && !string.IsNullOrEmpty(command.FilePath))
            dir = Path.GetDirectoryName(command.FilePath);

        if (dir == null)
        {
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "commands");
        }

        Directory.CreateDirectory(dir);

        var fileName = command.Name.Replace(':', Path.DirectorySeparatorChar) + ".md";
        var filePath = Path.Combine(dir, fileName);

        // Ensure subdirectory exists
        var fileDir = Path.GetDirectoryName(filePath);
        if (fileDir != null) Directory.CreateDirectory(fileDir);

        var sb = new System.Text.StringBuilder();

        // Write frontmatter if we have metadata
        if (!string.IsNullOrEmpty(command.Description) || command.AllowedTools.Count > 0)
        {
            sb.AppendLine("---");
            if (!string.IsNullOrEmpty(command.Description))
                sb.AppendLine($"description: \"{command.Description}\"");
            if (command.AllowedTools.Count > 0)
            {
                sb.AppendLine("allowed-tools:");
                foreach (var tool in command.AllowedTools)
                    sb.AppendLine($"  - {tool}");
            }
            sb.AppendLine("---");
            sb.AppendLine();
        }

        sb.Append(command.PromptTemplate);

        await File.WriteAllTextAsync(filePath, sb.ToString());
        command.FilePath = filePath;

        // Re-register
        Register(command);
    }

    public Task DeleteCommandAsync(string name, string scope, string? projectPath)
    {
        var skill = _skills.FirstOrDefault(s => s.Name == name && s.Scope == scope);
        if (skill?.FilePath != null && File.Exists(skill.FilePath))
        {
            File.Delete(skill.FilePath);
        }

        _skills.RemoveAll(s => s.Name == name && s.Scope == scope);
        return Task.CompletedTask;
    }
}
