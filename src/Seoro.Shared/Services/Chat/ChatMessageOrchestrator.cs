using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Chat;

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
        // --- Guard: session must be Ready (worktree already created on session creation) ---
        if (session.Status != SessionStatus.Ready)
        {
            logger.LogWarning("메시지 전송 불가: 세션 {SessionId}의 상태가 {Status}임", session.Id, session.Status);
            return new StreamResult();
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
            conversationId, false, ct, input.ModelOverride);

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
        CancellationToken ct,
        string? modelOverride = null)
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
                               modelOverride ?? session.Model,
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
            logger.LogInformation("{Mode} 세션 {SessionId}에 대해 취소됨",
                continueMode ? "계속 진행" : "메시지 처리", session.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "세션 {SessionId}의 {Mode} 중 오류 발생",
                continueMode ? "continue" : "message processing", session.Id);
            if (string.IsNullOrEmpty(assistantMsg.Text))
                chatState.AppendText(assistantMsg, $"Error: {ex.Message}");
            errorMessage = ex.Message;
        }
        finally
        {
            // Ensure pending tokens are cleared even on cancellation or error
            session.PendingInputTokens = 0;
            session.PendingOutputTokens = 0;

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
                    ["SEORO_SESSION_ID"] = session.Id,
                    ["SEORO_CITY_NAME"] = session.CityName
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OnMessageComplete 훅 실행 실패");
            }
        });
    }
}