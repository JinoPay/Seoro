using System.Text.RegularExpressions;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public partial class InstructionsService : IInstructionsService
{
    private readonly ILogger<InstructionsService> _logger;

    public InstructionsService(ILogger<InstructionsService> logger)
    {
        _logger = logger;
    }

    public string GetFilePath(ClaudeSettingsScope scope, string? projectPath = null)
    {
        return scope switch
        {
            ClaudeSettingsScope.Global => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "CLAUDE.md"),
            ClaudeSettingsScope.Project => Path.Combine(
                projectPath ?? throw new ArgumentException("projectPath required"), ".claude", "CLAUDE.md"),
            ClaudeSettingsScope.Local => Path.Combine(
                projectPath ?? throw new ArgumentException("projectPath required"), "CLAUDE.md"),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
    }

    public async Task SaveAsync(ClaudeSettingsScope scope, string content, string? projectPath = null)
    {
        var filePath = GetFilePath(scope, projectPath);
        await AtomicFileWriter.WriteAsync(filePath, content);
        _logger.LogDebug("Saved instructions: {Path}", filePath);
    }

    public Task<InstructionFile> ReadAsync(ClaudeSettingsScope scope, string? projectPath = null)
    {
        var filePath = GetFilePath(scope, projectPath);
        var exists = File.Exists(filePath);
        var content = exists ? File.ReadAllText(filePath) : "";
        var imports = ExtractImportRefs(content);

        return Task.FromResult(new InstructionFile
        {
            Scope = scope,
            FilePath = filePath,
            Content = content,
            Exists = exists,
            ImportRefs = imports
        });
    }

    private static List<string> ExtractImportRefs(string content)
    {
        if (string.IsNullOrEmpty(content)) return [];
        return ImportPattern().Matches(content)
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    [GeneratedRegex(@"@import\s+[""']?([^""'\s]+)[""']?")]
    private static partial Regex ImportPattern();
}