using System.Text;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Plugin;

public class SkillFileStore(ILogger logger)
{
    public async Task SaveAsync(SkillDefinition command)
    {
        var dir = command.Scope == "project"
            ? null
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "commands");

        if (dir == null && !string.IsNullOrEmpty(command.FilePath))
            dir = Path.GetDirectoryName(command.FilePath);

        if (dir == null)
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", "commands");

        Directory.CreateDirectory(dir);

        var fileName = command.Name.Replace(':', Path.DirectorySeparatorChar) + ".md";
        var filePath = Path.Combine(dir, fileName);

        // Ensure subdirectory exists
        var fileDir = Path.GetDirectoryName(filePath);
        if (fileDir != null) Directory.CreateDirectory(fileDir);

        var sb = new StringBuilder();

        // Write frontmatter if we have metadata
        if (!string.IsNullOrEmpty(command.Description) || command.AllowedTools.Count > 0 || command.Chain.Count > 0)
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

            if (command.Chain.Count > 0)
            {
                sb.AppendLine("chain:");
                foreach (var step in command.Chain)
                    sb.AppendLine($"  - {step}");
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        sb.Append(command.PromptTemplate);

        await AtomicFileWriter.WriteAsync(filePath, sb.ToString());
        command.FilePath = filePath;
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
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    var skill = ParseCommandFile(file, content, scope, directory);
                    if (skill != null)
                        skills.Add(skill);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "명령어 파일 파싱 실패: {File}", file);
                }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "명령어 디렉터리 스캔 실패: {Dir}", directory);
        }

        return skills;
    }

    public void Delete(SkillDefinition skill)
    {
        if (skill.FilePath != null && File.Exists(skill.FilePath)) File.Delete(skill.FilePath);
    }

    private static List<string> ParseInlineList(string value)
    {
        return value.Trim('[', ']')
            .Split(',')
            .Select(t => t.Trim().Trim('"', '\''))
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
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
        var description = "";
        var allowedTools = new List<string>();
        var chain = new List<string>();
        var body = content;

        var lines = content.Split('\n');
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var endIdx = -1;
            for (var i = 1; i < lines.Length; i++)
                if (lines[i].Trim() == "---")
                {
                    endIdx = i;
                    break;
                }

            if (endIdx > 0)
            {
                string? currentListField = null;

                for (var i = 1; i < endIdx; i++)
                {
                    var line = lines[i].Trim();
                    if (line.StartsWith("description:"))
                    {
                        currentListField = null;
                        description = line["description:".Length..].Trim().Trim('"', '\'');
                    }
                    else if (line.StartsWith("allowed-tools:"))
                    {
                        currentListField = "allowed-tools";
                        var val = line["allowed-tools:".Length..].Trim();
                        if (val.StartsWith('['))
                        {
                            allowedTools = ParseInlineList(val);
                            currentListField = null;
                        }
                    }
                    else if (line.StartsWith("chain:"))
                    {
                        currentListField = "chain";
                        var val = line["chain:".Length..].Trim();
                        if (val.StartsWith('['))
                        {
                            chain = ParseInlineList(val);
                            currentListField = null;
                        }
                    }
                    else if (line.StartsWith("- ") && currentListField != null)
                    {
                        var item = line[2..].Trim();
                        if (currentListField == "allowed-tools")
                            allowedTools.Add(item);
                        else if (currentListField == "chain")
                            chain.Add(item);
                    }
                    else if (!line.StartsWith("- "))
                    {
                        currentListField = null;
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
            Chain = chain,
            Namespace = ns,
            AcceptsArguments = acceptsArguments,
            FilePath = filePath
        };
    }
}