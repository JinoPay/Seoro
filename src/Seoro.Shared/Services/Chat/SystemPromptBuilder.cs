using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Chat;

public class SystemPromptBuilder(
    IContextService contextService,
    IMemoryService memoryService,
    ICliProviderFactory cliProviderFactory,
    ILogger<SystemPromptBuilder> logger)
    : ISystemPromptBuilder
{
    public async Task<string?> BuildAsync(Session session, Workspace? workspace)
    {
        var parts = new List<string>();

        // 워크트리 세션에서 브랜치가 아직 초기 타임스탬프 이름일 때만 이름 변경 지시 주입
        if (!session.Git.IsLocalDir && SeoroConstants.IsTimestampBranch(session.Git.BranchName))
            parts.Add(SeoroConstants.GetSystemInstructionWorktree());

        if (!session.Git.IsLocalDir && !string.IsNullOrEmpty(session.Git.WorktreePath))
            parts.Add(string.Format(SeoroConstants.SystemInstructionWorktreeDir, session.Git.WorktreePath));

        var wsPrompt = workspace?.SystemPrompt;
        if (!string.IsNullOrWhiteSpace(wsPrompt))
            parts.Add(wsPrompt);

        var generalPrompt = workspace?.Preferences?.GeneralPrompt;
        if (!string.IsNullOrWhiteSpace(generalPrompt))
            parts.Add(generalPrompt);

        var provider = cliProviderFactory.GetProviderForSession(session);
        if (provider.Capabilities.SupportsPlanMode && session.PermissionMode == "plan")
            parts.Add(session.IsCodex
                ? @"You are in Plan mode. Your goal is to explore the codebase thoroughly and return a detailed implementation plan in your final response.

Rules:
- Use read-only tools to explore the codebase
- Do NOT modify any source files
- Do NOT create or update plan files unless the user explicitly asks for that
- Structure your final response with: Context, Changes (specific files and what to change), and Verification steps"
                : @"You are in Plan mode. Your goal is to explore the codebase thoroughly and create a detailed implementation plan.

Rules:
- Use read-only tools (Read, Grep, Glob, Bash for read-only commands) to explore the codebase
- Create a detailed implementation plan and save it to .claude/plans/ directory
- Do NOT modify any source files — only create/update plan files
- When your plan is complete and ready for review, call the ExitPlanMode tool to signal completion
- Structure your plan with: Context, Changes (specific files and what to change), and Verification steps");
        else if (session.AgentType == AgentType.Explore)
            parts.Add("You are in Explore mode. Only read and search — do not modify any files.");

        if (!string.IsNullOrEmpty(session.Git.WorktreePath) && Directory.Exists(session.Git.WorktreePath))
            try
            {
                var context = await contextService.LoadContextAsync(session.Git.WorktreePath);
                var contextPrompt = contextService.BuildContextPrompt(context);
                if (!string.IsNullOrWhiteSpace(contextPrompt))
                    parts.Add(contextPrompt);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "시스템 프롬프트의 컨텍스트 로드 실패");
            }

        try
        {
            var memories = await memoryService.GetForWorkspaceAsync(workspace?.Id);
            if (memories.Count > 0)
            {
                var memoryPrompt = memoryService.BuildMemoryPrompt(memories);
                parts.Add(memoryPrompt);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "시스템 프롬프트의 메모리 로드 실패");
        }

        if (parts.Count == 0) return null;

        var combined = string.Join("\n\n", parts);
        return TokenEstimator.Estimate(combined) <= SeoroConstants.MaxSystemPromptTokens
            ? combined
            : TokenEstimator.Truncate(combined, SeoroConstants.MaxSystemPromptTokens);
    }
}
