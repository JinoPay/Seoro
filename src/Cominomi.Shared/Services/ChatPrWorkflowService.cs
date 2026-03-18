using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cominomi.Shared.Services;

public class ChatPrWorkflowService : IChatPrWorkflowService
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ISessionGitWorkflowService _gitWorkflow;
    private readonly IGhService _ghService;
    private readonly ISessionService _sessionService;
    private readonly IOptionsMonitor<AppSettings> _appSettings;
    private readonly ILogger<ChatPrWorkflowService> _logger;

    public ChatPrWorkflowService(
        IWorkspaceService workspaceService,
        ISessionGitWorkflowService gitWorkflow,
        IGhService ghService,
        ISessionService sessionService,
        IOptionsMonitor<AppSettings> appSettings,
        ILogger<ChatPrWorkflowService> logger)
    {
        _workspaceService = workspaceService;
        _gitWorkflow = gitWorkflow;
        _ghService = ghService;
        _sessionService = sessionService;
        _appSettings = appSettings;
        _logger = logger;
    }

    public async Task<string> BuildCreatePrPromptAsync(Session session)
    {
        var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
        var preferences = workspace?.CreatePrPreferences;

        var prompt = "PR을 생성해주세요. diff를 확인하고, 커밋 상태를 점검하고, 브랜치를 푸시한 뒤 `gh pr create`로 PR을 만들어주세요.";

        if (session.Pr.IssueNumber != null)
            prompt += $"\n\n연결된 이슈: #{session.Pr.IssueNumber}";

        if (!string.IsNullOrEmpty(preferences))
            prompt += $"\n\n## PR 생성 지침\n{preferences}";

        return prompt;
    }

    public async Task<(SessionStatus Status, AppError? Error)> MergePrAsync(Session session)
    {
        var mergeMethod = _appSettings.CurrentValue.DefaultMergeStrategy ?? "squash";

        var updated = await _gitWorkflow.MergePrAsync(session.Id, mergeMethod);
        return (updated.Status, updated.Error);
    }

    public async Task<(SessionStatus Status, AppError? Error)> ForcePushAsync(Session session)
    {
        var pushed = await _gitWorkflow.PushBranchAsync(session.Id, force: true);
        return (pushed.Status, pushed.Error);
    }

    public async Task<(Session? FullSession, string RebasePrompt)> ResolveConflictsAsync(Session session)
    {
        await _gitWorkflow.RetryAfterConflictResolveAsync(session.Id);

        var fullSession = await _sessionService.LoadSessionAsync(session.Id);
        var baseBranch = !string.IsNullOrEmpty(fullSession?.Git.BaseBranch) ? fullSession!.Git.BaseBranch : "main";
        var prompt = $"{baseBranch} 브랜치와의 충돌로 PR 병합에 실패했습니다. 이 브랜치를 origin/{baseBranch}에 리베이스하고 충돌을 해결한 후 결과를 커밋해 주세요.";

        return (fullSession, prompt);
    }

    public async Task<(int? PrNumber, string? PrUrl)?> CheckPrStatusAsync(Session session)
    {
        try
        {
            var workspace = await _workspaceService.LoadWorkspaceAsync(session.WorkspaceId);
            if (workspace == null) return null;

            var prInfo = await _ghService.GetPrForBranchAsync(workspace.RepoLocalPath, session.Git.BranchName);
            if (prInfo != null && prInfo.State is "OPEN" or "open")
                return (prInfo.Number, prInfo.Url);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check PR status for session {SessionId}", session.Id);
            return null;
        }
    }
}
