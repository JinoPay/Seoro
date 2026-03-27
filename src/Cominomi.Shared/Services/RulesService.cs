using System.Text.RegularExpressions;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public partial class RulesService : IRulesService
{
    private readonly ILogger<RulesService> _logger;

    public RulesService(ILogger<RulesService> logger)
    {
        _logger = logger;
    }

    public Task<List<RuleFile>> ListAsync(ClaudeSettingsScope scope, string? projectPath = null)
    {
        var dir = GetRulesDirectory(scope, projectPath);
        var rules = new List<RuleFile>();

        if (!Directory.Exists(dir))
            return Task.FromResult(rules);

        foreach (var file in Directory.GetFiles(dir, "*.md"))
        {
            try
            {
                var content = File.ReadAllText(file);
                var pathFilters = ExtractPathFilters(content);

                rules.Add(new RuleFile
                {
                    FileName = Path.GetFileName(file),
                    FilePath = file,
                    Scope = scope,
                    Content = content,
                    PathFilters = pathFilters
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read rule file: {Path}", file);
            }
        }

        return Task.FromResult(rules);
    }

    public Task<RuleFile?> ReadAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return Task.FromResult<RuleFile?>(null);

        var content = File.ReadAllText(filePath);
        var scope = filePath.Contains(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"))
            ? ClaudeSettingsScope.Global
            : ClaudeSettingsScope.Project;

        return Task.FromResult<RuleFile?>(new RuleFile
        {
            FileName = Path.GetFileName(filePath),
            FilePath = filePath,
            Scope = scope,
            Content = content,
            PathFilters = ExtractPathFilters(content)
        });
    }

    public async Task SaveAsync(RuleFile rule)
    {
        var dir = Path.GetDirectoryName(rule.FilePath);
        if (dir != null)
            Directory.CreateDirectory(dir);

        await AtomicFileWriter.WriteAsync(rule.FilePath, rule.Content);
        _logger.LogDebug("Saved rule file: {Path}", rule.FilePath);
    }

    public Task DeleteAsync(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogDebug("Deleted rule file: {Path}", filePath);
        }
        return Task.CompletedTask;
    }

    public string GetRulesDirectory(ClaudeSettingsScope scope, string? projectPath = null)
    {
        return scope switch
        {
            ClaudeSettingsScope.Global => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "rules"),
            ClaudeSettingsScope.Project or ClaudeSettingsScope.Local =>
                Path.Combine(projectPath ?? throw new ArgumentException("projectPath required"), ".claude", "rules"),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
    }

    /// <summary>
    /// Extracts path filter patterns from frontmatter-like comments at the top of rule files.
    /// Looks for lines like: globs: src/**/*.ts, **/*.test.*
    /// </summary>
    private static List<string> ExtractPathFilters(string content)
    {
        var match = GlobsPattern().Match(content);
        if (!match.Success) return [];

        return match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    [GeneratedRegex(@"^globs:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex GlobsPattern();
}
