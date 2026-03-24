using System.Text;
using Cominomi.Shared;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public static class ClaudeArgumentBuilder
{
    /// <summary>
    /// Tools that require explicit --allowedTools to be usable,
    /// even when --dangerously-skip-permissions is set.
    /// </summary>
    private static readonly string[] DefaultAllowedTools =
    [
        "WebFetch",
        "WebSearch"
    ];

    public static string Build(
        string baseArgs,
        string model,
        string permissionMode,
        CliCapabilities caps,
        string? conversationId = null,
        string? systemPrompt = null,
        string effortLevel = CominomiConstants.DefaultEffortLevel,
        bool continueMode = false,
        bool forkSession = false,
        int? maxTurns = null,
        decimal? maxBudgetUsd = null,
        string? fallbackModel = null,
        string? mcpConfigPath = null,
        bool debugMode = false,
        List<string>? additionalDirs = null,
        List<string>? allowedTools = null,
        List<string>? disallowedTools = null)
    {
        var sb = new StringBuilder(baseArgs);
        sb.Append("--print --output-format stream-json ");
        if (caps.SupportsVerbose)
            sb.Append("--verbose ");
        if (debugMode)
            sb.Append("--debug ");
        sb.Append($"--model {model}");

        switch (permissionMode)
        {
            case "plan":
                sb.Append(" --permission-mode plan");
                break;
            case "acceptEdits":
                sb.Append(" --permission-mode acceptEdits");
                break;
            case "dontAsk":
                sb.Append(" --permission-mode dontAsk");
                break;
            case "bypassPermissions":
                sb.Append(" --permission-mode bypassPermissions");
                break;
            case "bypassAll":
                sb.Append(" --dangerously-skip-permissions");
                break;
            // "default" — no flag needed
        }

        if (!string.IsNullOrEmpty(effortLevel) && effortLevel != "auto")
            sb.Append($" --effort {effortLevel}");

        // Resume or continue existing conversation
        if (!string.IsNullOrEmpty(conversationId))
        {
            sb.Append($" --resume {conversationId}");
            if (continueMode)
                sb.Append(" --continue");
            if (forkSession)
                sb.Append(" --fork-session");
        }
        else if (continueMode)
            sb.Append(" --continue");

        // Turn and budget limits
        if (maxTurns.HasValue)
            sb.Append($" --max-turns {maxTurns.Value}");

        if (maxBudgetUsd.HasValue)
            sb.Append($" --max-budget-usd {maxBudgetUsd.Value:F2}");

        // Fallback model for overload resilience
        if (!string.IsNullOrEmpty(fallbackModel))
            sb.Append($" --fallback-model {fallbackModel}");

        // MCP server configuration
        if (!string.IsNullOrEmpty(mcpConfigPath))
            sb.Append($" --mcp-config \"{mcpConfigPath}\"");

        // Additional directories
        if (additionalDirs is { Count: > 0 })
        {
            foreach (var dir in additionalDirs)
                sb.Append($" --add-dir \"{dir}\"");
        }

        // Tool restrictions: always include default allowed tools, then caller-specified ones
        foreach (var tool in DefaultAllowedTools)
            sb.Append($" --allowedTools \"{tool}\"");
        if (allowedTools is { Count: > 0 })
        {
            foreach (var tool in allowedTools)
                sb.Append($" --allowedTools \"{tool}\"");
        }
        if (disallowedTools is { Count: > 0 })
        {
            foreach (var tool in disallowedTools)
                sb.Append($" --disallowedTools \"{tool}\"");
        }

        // System prompt — escape all characters that could break argument parsing
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            var escaped = systemPrompt
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r\n", "\\n")
                .Replace("\r", "\\n")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t")
                .Replace("\0", "");
            sb.Append($" --append-system-prompt \"{escaped}\"");
        }

        return sb.ToString();
    }
}
