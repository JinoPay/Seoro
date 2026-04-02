using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ChatMessageOrchestrator(
    IChatState chatState,
    IClaudeService claudeService,
    ISessionService sessionService,
    IAttachmentService attachmentService,
    IStreamEventProcessor streamProcessor,
    ISystemPromptBuilder systemPromptBuilder,
    IHooksEngine hooksEngine,
    IActiveSessionRegistry activeSessionRegistry,
    IGitBranchWatcherService branchWatcher,
    ILogger<ChatMessageOrchestrator> logger)
    : IChatMessageOrchestrator
{
    public async Task<StreamResult> ContinueAsync(
        Session session,
        Workspace? workspace,
        CancellationToken ct = default)
    {
        chatState.AddSystemMessage(session, "계속 진행 중...");
        chatState.SetStreaming(true, session.Id);
        chatState.SetPhase(StreamingPhase.Sending, sessionId: session.Id);
        var assistantMsg = chatState.StartAssistantMessage(session);
        activeSessionRegistry.Register(session);

        var systemPrompt = await systemPromptBuilder.BuildAsync(session, workspace);

        return await RunStreamingLoopAsync(
            session, assistantMsg, systemPrompt, string.Empty,
            session.ConversationId, true, ct);
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
                chatState.SetStreaming(true, session.Id);
                chatState.SetPhase(StreamingPhase.Preparing, sessionId: session.Id);

                var updated = await sessionService.InitializeWorktreeAsync(session.Id, selectedBranch);
                session.Git.WorktreePath = updated.Git.WorktreePath;
                session.Git.BranchName = updated.Git.BranchName;
                session.Git.BaseBranch = updated.Git.BaseBranch;
                session.SetInitialStatus(updated.Status);
                session.Error = updated.Error;

                if (session.Status == SessionStatus.Error)
                {
                    chatState.SetStreaming(false, session.Id);
                    chatState.NotifyStateChanged();
                    return new StreamResult();
                }
            }
            catch (Exception ex)
            {
                session.TransitionStatus(SessionStatus.Error);
                session.Error = AppError.FromException(ErrorCode.WorktreeCreationFailed, ex);
                chatState.SetStreaming(false, session.Id);
                chatState.NotifyStateChanged();
                return new StreamResult();
            }
        }

        ct.ThrowIfCancellationRequested();

        // --- Attachment handling ---
        var fileAttachments = new List<FileAttachment>();
        foreach (var pending in input.Attachments)
        {
            FileAttachment attachment;
            if (pending is { FilePath: not null, Data.Length: 0 })
                attachment =
                    await attachmentService.CopyFileToWorktreeAsync(pending.FilePath, session.Git.WorktreePath);
            else
                attachment = await attachmentService.SaveBytesToWorktreeAsync(
                    pending.Data, pending.FileName, pending.ContentType, session.Git.WorktreePath);
            fileAttachments.Add(attachment);
        }

        var messageForClaude = attachmentService.BuildMessageWithAttachments(input.Text, fileAttachments);

        // --- User message ---
        if (fileAttachments.Count > 0)
            chatState.AddUserMessage(session, input.Text, fileAttachments);
        else
            chatState.AddUserMessage(session, input.Text);

        await sessionService.SaveSessionAsync(session);
        activeSessionRegistry.Register(session);

        // --- Streaming setup ---
        chatState.SetStreaming(true, session.Id);
        chatState.SetPhase(StreamingPhase.Sending, sessionId: session.Id);
        var assistantMsg = chatState.StartAssistantMessage(session);

        // --- Stream + finalize ---
        var conversationId = session.ConversationId;
        var systemPrompt = await systemPromptBuilder.BuildAsync(session, workspace);

        var result = await RunStreamingLoopAsync(
            session, assistantMsg, systemPrompt, messageForClaude,
            conversationId, false, ct);

        // --- Post-stream: hooks ---
        FireHooksInBackground(session);

        return result;
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
        var firstEvent = true;
        var streamCtx = new StreamProcessingContext
        {
            Session = session,
            AssistantMessage = assistantMsg,
            StreamStartTime = DateTime.UtcNow
        };
        string? errorMessage = null;
        var wasCancelled = false;

        try
        {
            ct.ThrowIfCancellationRequested();

            await foreach (var evt in claudeService.SendMessageAsync(
                               message,
                               session.Git.WorktreePath,
                               session.Model,
                               session.PermissionMode,
                               session.EffortLevel ?? "auto",
                               session.Id,
                               conversationId,
                               systemPrompt,
                               continueMode).WithCancellation(ct))
            {
                if (firstEvent)
                {
                    chatState.SetPhase(StreamingPhase.Thinking, sessionId: session.Id);
                    firstEvent = false;
                }

                await streamProcessor.ProcessEventAsync(evt, streamCtx);
            }

            await streamProcessor.FinalizeAsync(streamCtx);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            wasCancelled = true;
            logger.LogInformation("{Mode} cancelled for session {SessionId}",
                continueMode ? "Continue" : "Message processing", session.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during {Mode} for session {SessionId}",
                continueMode ? "continue" : "message processing", session.Id);
            if (string.IsNullOrEmpty(assistantMsg.Text))
                chatState.AppendText(assistantMsg, $"Error: {ex.Message}");
            errorMessage = ex.Message;
        }
        finally
        {
            chatState.FinishMessage(assistantMsg);
            chatState.SetStreaming(false, session.Id);

            chatState.NotifyStateChanged();
            await sessionService.SaveSessionAsync(session);
            activeSessionRegistry.Unregister(session.Id);
            _ = branchWatcher.RefreshBranchAsync(session);
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
                await hooksEngine.FireAsync(HookEvent.OnMessageComplete, new Dictionary<string, string>
                {
                    ["COMINOMI_SESSION_ID"] = session.Id,
                    ["COMINOMI_CITY_NAME"] = session.CityName
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Hook fire failed for OnMessageComplete");
            }
        });
    }
}