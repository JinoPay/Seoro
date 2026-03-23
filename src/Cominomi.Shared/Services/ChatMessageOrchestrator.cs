using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ChatMessageOrchestrator : IChatMessageOrchestrator
{
    private readonly IChatState _chatState;
    private readonly IClaudeService _claudeService;
    private readonly ISessionService _sessionService;
    private readonly IAttachmentService _attachmentService;
    private readonly IStreamEventProcessor _streamProcessor;
    private readonly ISystemPromptBuilder _systemPromptBuilder;
    private readonly ISessionInitializer _sessionInitializer;
    private readonly IHooksEngine _hooksEngine;
    private readonly IChatPrWorkflowService _prWorkflow;
    private readonly IActiveSessionRegistry _activeSessionRegistry;
    private readonly ILogger<ChatMessageOrchestrator> _logger;

    public ChatMessageOrchestrator(
        IChatState chatState,
        IClaudeService claudeService,
        ISessionService sessionService,
        IAttachmentService attachmentService,
        IStreamEventProcessor streamProcessor,
        ISystemPromptBuilder systemPromptBuilder,
        ISessionInitializer sessionInitializer,
        IHooksEngine hooksEngine,
        IChatPrWorkflowService prWorkflow,
        IActiveSessionRegistry activeSessionRegistry,
        ILogger<ChatMessageOrchestrator> logger)
    {
        _chatState = chatState;
        _claudeService = claudeService;
        _sessionService = sessionService;
        _attachmentService = attachmentService;
        _streamProcessor = streamProcessor;
        _systemPromptBuilder = systemPromptBuilder;
        _sessionInitializer = sessionInitializer;
        _hooksEngine = hooksEngine;
        _prWorkflow = prWorkflow;
        _activeSessionRegistry = activeSessionRegistry;
        _logger = logger;
    }

    public async Task<StreamResult> SendAsync(
        Session session,
        ChatInputMessage input,
        string selectedBranch,
        Workspace? workspace,
        CancellationToken ct = default)
    {
        // --- Worktree init (Pending sessions) ---
        if (session.Status == SessionStatus.Pending)
        {
            if (string.IsNullOrEmpty(selectedBranch))
                return new StreamResult();

            try
            {
                _chatState.SetStreaming(true, session.Id);
                _chatState.SetPhase(StreamingPhase.Preparing, sessionId: session.Id);

                var updated = await _sessionService.InitializeWorktreeAsync(session.Id, selectedBranch);
                session.Git.WorktreePath = updated.Git.WorktreePath;
                session.Git.BranchName = updated.Git.BranchName;
                session.Git.BaseBranch = updated.Git.BaseBranch;
                session.SetInitialStatus(updated.Status);
                session.Error = updated.Error;

                if (session.Status == SessionStatus.Error)
                {
                    _chatState.SetStreaming(false, session.Id);
                    _chatState.NotifyStateChanged();
                    return new StreamResult();
                }
            }
            catch (Exception ex)
            {
                session.TransitionStatus(SessionStatus.Error);
                session.Error = AppError.FromException(ErrorCode.WorktreeCreationFailed, ex);
                _chatState.SetStreaming(false, session.Id);
                _chatState.NotifyStateChanged();
                return new StreamResult();
            }
        }

        ct.ThrowIfCancellationRequested();

        // --- Attachment handling ---
        var fileAttachments = new List<FileAttachment>();
        foreach (var pending in input.Attachments)
        {
            FileAttachment attachment;
            if (pending.FilePath != null && pending.Data.Length == 0)
                attachment = await _attachmentService.CopyFileToWorktreeAsync(pending.FilePath, session.Git.WorktreePath);
            else
                attachment = await _attachmentService.SaveBytesToWorktreeAsync(
                    pending.Data, pending.FileName, pending.ContentType, session.Git.WorktreePath);
            fileAttachments.Add(attachment);
        }

        var messageForClaude = _attachmentService.BuildMessageWithAttachments(input.Text, fileAttachments);

        // --- User message ---
        if (fileAttachments.Count > 0)
            _chatState.AddUserMessage(session, input.Text, fileAttachments);
        else
            _chatState.AddUserMessage(session, input.Text);

        await _sessionService.SaveSessionAsync(session);
        _activeSessionRegistry.Register(session);

        // --- Streaming setup ---
        var isFirstMessage = session.Messages.Count(m => m.Role == MessageRole.User) == 0;

        _chatState.SetStreaming(true, session.Id);
        _chatState.SetPhase(StreamingPhase.Sending, sessionId: session.Id);
        var assistantMsg = _chatState.StartAssistantMessage(session);

        // --- First message: title + branch rename ---
        if (isFirstMessage && session.Status == SessionStatus.Ready && !string.IsNullOrWhiteSpace(input.Text))
        {
            _chatState.SetPhase(StreamingPhase.Preparing, sessionId: session.Id);
            var (title, newBranch) = await _sessionInitializer.SummarizeAndRenameBranchAsync(session, input.Text);
            if (!string.IsNullOrEmpty(title))
            {
                session.Title = title;
                if (newBranch != null)
                    session.Git.BranchName = newBranch;

                _chatState.Tabs.UpdateChatTabTitle(title);

                var branchInfo = session.Git.IsLocalDir ? "" : $" · 브랜치: {session.Git.BranchName}";
                _chatState.AddSystemMessage(session, $"제목이 \"{title}\"(으)로 설정됨{branchInfo}");

                await _sessionService.SaveSessionAsync(session);
            }
            _chatState.SetPhase(StreamingPhase.Sending, sessionId: session.Id);
        }

        // --- Stream + finalize ---
        var conversationId = session.ConversationId;
        var systemPrompt = await _systemPromptBuilder.BuildAsync(session, workspace);

        var result = await RunStreamingLoopAsync(
            session, assistantMsg, systemPrompt, messageForClaude,
            conversationId, continueMode: false, ct);

        // --- Post-stream: hooks + PR check ---
        FireHooksInBackground(session);

        if (session.Status == SessionStatus.Pushed && !string.IsNullOrEmpty(session.Git.BranchName))
            _ = CheckAndUpdatePrStatusAsync(session);

        return result;
    }

    public async Task<StreamResult> ContinueAsync(
        Session session,
        Workspace? workspace,
        CancellationToken ct = default)
    {
        _chatState.AddSystemMessage(session, "계속 진행 중...");
        _chatState.SetStreaming(true, session.Id);
        _chatState.SetPhase(StreamingPhase.Sending, sessionId: session.Id);
        var assistantMsg = _chatState.StartAssistantMessage(session);
        _activeSessionRegistry.Register(session);

        var systemPrompt = await _systemPromptBuilder.BuildAsync(session, workspace);

        return await RunStreamingLoopAsync(
            session, assistantMsg, systemPrompt, string.Empty,
            session.ConversationId, continueMode: true, ct);
    }

    public async Task CheckAndUpdatePrStatusAsync(Session session)
    {
        try
        {
            var result = await _prWorkflow.CheckPrStatusAsync(session);
            if (result is var (prNumber, prUrl))
            {
                session.TransitionStatus(SessionStatus.PrOpen);
                session.Pr.PrUrl = prUrl;
                session.Pr.PrNumber = prNumber;
                await _sessionService.SaveSessionAsync(session);
                _chatState.NotifyStateChanged();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PR status check failed for session {SessionId}", session.Id);
        }
    }

    // ──────────────────────────────────────────────
    //  Private: shared streaming core
    // ──────────────────────────────────────────────

    private async Task<StreamResult> RunStreamingLoopAsync(
        Session session,
        ChatMessage assistantMsg,
        string? systemPrompt,
        string message,
        string? conversationId,
        bool continueMode,
        CancellationToken ct)
    {
        bool firstEvent = true;
        var streamCtx = new StreamProcessingContext
        {
            Session = session,
            AssistantMessage = assistantMsg,
            StreamStartTime = DateTime.UtcNow
        };
        string? errorMessage = null;
        bool wasCancelled = false;

        try
        {
            ct.ThrowIfCancellationRequested();

            await foreach (var evt in _claudeService.SendMessageAsync(
                message,
                session.Git.WorktreePath,
                session.Model,
                session.PermissionMode,
                effortLevel: session.EffortLevel ?? "auto",
                sessionId: session.Id,
                conversationId: conversationId,
                systemPrompt: systemPrompt,
                continueMode: continueMode).WithCancellation(ct))
            {
                if (firstEvent)
                {
                    _chatState.SetPhase(StreamingPhase.Thinking, sessionId: session.Id);
                    firstEvent = false;
                }

                await _streamProcessor.ProcessEventAsync(evt, streamCtx);
            }

            await _streamProcessor.FinalizeAsync(streamCtx);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            wasCancelled = true;
            _logger.LogInformation("{Mode} cancelled for session {SessionId}",
                continueMode ? "Continue" : "Message processing", session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during {Mode} for session {SessionId}",
                continueMode ? "continue" : "message processing", session.Id);
            if (string.IsNullOrEmpty(assistantMsg.Text))
                _chatState.AppendText(assistantMsg, $"Error: {ex.Message}");
            errorMessage = ex.Message;
        }
        finally
        {
            _chatState.FinishMessage(assistantMsg);
            _chatState.SetStreaming(false, session.Id);

            _chatState.NotifyStateChanged();
            await _sessionService.SaveSessionAsync(session);
            _activeSessionRegistry.Unregister(session.Id);
        }

        return new StreamResult
        {
            PlanFilePath = streamCtx.PlanFilePath,
            PlanContent = streamCtx.PlanContent,
            PlanReviewVisible = streamCtx.PlanReviewVisible,
            QuickResponseVisible = streamCtx.QuickResponseVisible,
            QuickResponseOptions = streamCtx.QuickResponseOptions,
            AskUserQuestionInput = streamCtx.AskUserQuestionInput,
            ErrorMessage = errorMessage,
            WasCancelled = wasCancelled
        };
    }

    private void FireHooksInBackground(Session session)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _hooksEngine.FireAsync(HookEvent.OnMessageComplete, new Dictionary<string, string>
                {
                    ["COMINOMI_SESSION_ID"] = session.Id,
                    ["COMINOMI_CITY_NAME"] = session.CityName
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hook fire failed for OnMessageComplete");
            }
        });
    }
}
