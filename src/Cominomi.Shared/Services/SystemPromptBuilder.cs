using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class SystemPromptBuilder : ISystemPromptBuilder
{
    private readonly IContextService _contextService;
    private readonly IMemoryService _memoryService;
    private readonly ILogger<SystemPromptBuilder> _logger;

    public SystemPromptBuilder(
        IContextService contextService,
        IMemoryService memoryService,
        ILogger<SystemPromptBuilder> logger)
    {
        _contextService = contextService;
        _memoryService = memoryService;
        _logger = logger;
    }

    public async Task<string?> BuildAsync(Session session, Workspace? workspace)
    {
        var parts = new List<string>();

        var wsPrompt = workspace?.SystemPrompt;
        if (!string.IsNullOrWhiteSpace(wsPrompt))
            parts.Add(wsPrompt);

        if (session.PermissionMode == "plan")
        {
            parts.Add(@"You are in Plan mode. Your goal is to explore the codebase thoroughly and create a detailed implementation plan.

Rules:
- Use read-only tools (Read, Grep, Glob, Bash for read-only commands) to explore the codebase
- Create a detailed implementation plan and save it to .claude/plans/ directory
- Do NOT modify any source files — only create/update plan files
- When your plan is complete and ready for review, call the ExitPlanMode tool to signal completion
- Structure your plan with: Context, Changes (specific files and what to change), and Verification steps");
        }
        else if (session.AgentType == AgentType.Explore)
            parts.Add("You are in Explore mode. Only read and search — do not modify any files.");

        if (!string.IsNullOrEmpty(session.Git.WorktreePath) && Directory.Exists(session.Git.WorktreePath))
        {
            try
            {
                var context = await _contextService.LoadContextAsync(session.Git.WorktreePath);
                var contextPrompt = _contextService.BuildContextPrompt(context);
                if (!string.IsNullOrWhiteSpace(contextPrompt))
                    parts.Add(contextPrompt);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to load context for system prompt"); }
        }

        try
        {
            var memories = await _memoryService.GetAllAsync();
            if (memories.Count > 0)
            {
                var memoryPrompt = _memoryService.BuildMemoryPrompt(memories);
                parts.Add(memoryPrompt);
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to load memory for system prompt"); }

        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }
}
