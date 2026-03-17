using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SkillFileStore
{
    private readonly ILogger _logger;

    public SkillFileStore(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<List<SkillDefinition>> LoadFromDirectoryAsync(string directory, string scope)
    {
        var skills = new List<SkillDefinition>();
        if (!Directory.Exists(directory))
            return skills;

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
                        skills.Add(skill);
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

        return skills;
    }

    public async Task SaveAsync(SkillDefinition command)
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
    }

    public void Delete(SkillDefinition skill)
    {
        if (skill.FilePath != null && File.Exists(skill.FilePath))
        {
            File.Delete(skill.FilePath);
        }
    }

    internal static SkillDefinition? ParseCommandFile(string filePath, string content, string scope, string baseDir)
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
}
