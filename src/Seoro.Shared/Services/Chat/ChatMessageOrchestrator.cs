using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Chat;

public class ChatMessageOrchestrator(
    IChatState chatState,
    IClaudeService claudeService,
    ICliProviderFactory cliProviderFactory,
    ISessionService sessionService,
    IAttachmentService attachmentService,
    IStreamEventProcessor streamProcessor,
    ISystemPromptBuilder systemPromptBuilder,
    IHooksEngine hooksEngine,
    IPullRequestService pullRequestService,
    IActiveSessionRegistry activeSessionRegistry,
    IGitBranchWatcherService branchWatcher,
    IClaudeSettingsService claudeSettingsService,
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
        // --- 보호: 세션은 Ready 상태여야 (워크트리는 세션 생성 시 미리 생성됨) ---
        if (session.Status != SessionStatus.Ready)
        {
            logger.LogWarning("메시지 전송 불가: 세션 {SessionId}의 상태가 {Status}임", session.Id, session.Status);
            return new StreamResult();
        }

        ct.ThrowIfCancellationRequested();

        // --- 첨부파일 처리 ---
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

        // --- 사용자 메시지 ---
        if (fileAttachments.Count > 0)
            chatState.AddUserMessage(session, input.Text, fileAttachments);
        else
            chatState.AddUserMessage(session, input.Text);

        await sessionService.SaveSessionAsync(session);
        activeSessionRegistry.Register(session);

        // --- 스트리밍 설정 ---
        chatState.SetStreaming(true, session.Id);
        chatState.SetPhase(StreamingPhase.Sending, sessionId: session.Id);
        var assistantMsg = chatState.StartAssistantMessage(session);

        // --- 스트림 + 완료 ---
        var conversationId = session.ConversationId;
        var systemPrompt = await systemPromptBuilder.BuildAsync(session, workspace);

        var result = await RunStreamingLoopAsync(
            session, assistantMsg, systemPrompt, messageForClaude,
            conversationId, false, ct, input.ModelOverride);

        // --- 스트림 후: 훅 ---
        FireHooksInBackground(session);

        return result;
    }

    // ──────────────────────────────────────────────
    //  비공개: 공유 스트리밍 핵심
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

            var provider = cliProviderFactory.GetProviderForSession(session);
            var permissionMode = session.PermissionMode ?? SeoroConstants.DefaultPermissionMode;
            List<string>? mcpAllowedTools = null;
            if (permissionMode == "bypassAll")
            {
                mcpAllowedTools = await CollectMcpToolPatternsAsync(session.Git.WorktreePath);
                // 세션에서 비활성화된 MCP 서버 패턴 제거
                if (session.DisabledMcpServers is { Count: > 0 } disabled && mcpAllowedTools != null)
                    mcpAllowedTools.RemoveAll(p =>
                        disabled.Any(s => p.Equals($"mcp__{s}__*", StringComparison.OrdinalIgnoreCase)));
            }

            var sendOptions = new CliSendOptions
            {
                Message = message,
                WorkingDir = session.Git.WorktreePath,
                Model = modelOverride ?? session.Model,
                PermissionMode = permissionMode,
                EffortLevel = session.EffortLevel ?? SeoroConstants.DefaultEffortLevel,
                SessionId = session.Id,
                ConversationId = conversationId,
                SystemPrompt = systemPrompt,
                ContinueMode = continueMode,
                AllowedTools = mcpAllowedTools
            };

            await foreach (var evt in provider.SendMessageAsync(sendOptions, ct).WithCancellation(ct))
            {
                if (firstEvent)
                {
                    chatState.SetPhase(StreamingPhase.Thinking, sessionId: session.Id);
                    firstEvent = false;
                }

                await streamProcessor.ProcessEventAsync(evt, streamCtx);

                if (streamCtx.ShouldBreakStream)
                    break;
            }

            await streamProcessor.FinalizeAsync(streamCtx);

            // PR 자동 추적: 기존 TrackedPr 이 있으면 갱신, 없으면 AI 응답에서 캡처 시도.
            try
            {
                if (session.Git.TrackedPr != null)
                    await pullRequestService.RefreshAsync(session, ct);
                else
                    await pullRequestService.TryCaptureCreatedPrAsync(session, assistantMsg, ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "세션 {SessionId} PR 추적 갱신 실패", session.Id);
            }
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
                continueMode ? "계속 진행" : "메시지 처리", session.Id);
            if (string.IsNullOrEmpty(assistantMsg.Text))
                chatState.AppendText(assistantMsg, $"Error: {ex.Message}");
            errorMessage = ex.Message;
        }
        finally
        {
            // 취소 또는 오류 발생 시에도 대기 중인 토큰 정리
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

    private async Task<List<string>?> CollectMcpToolPatternsAsync(string? projectPath)
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var global = await claudeSettingsService.ReadAsync(ClaudeSettingsScope.Global);
            if (global.McpServers != null)
                foreach (var key in global.McpServers.Keys)
                    patterns.Add($"mcp__{key}__*");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Global MCP 서버 목록 읽기 실패");
        }

        if (!string.IsNullOrEmpty(projectPath))
        {
            try
            {
                var project = await claudeSettingsService.ReadAsync(ClaudeSettingsScope.Project, projectPath);
                if (project.McpServers != null)
                    foreach (var key in project.McpServers.Keys)
                        patterns.Add($"mcp__{key}__*");
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Project MCP 서버 목록 읽기 실패");
            }

            try
            {
                var local = await claudeSettingsService.ReadAsync(ClaudeSettingsScope.Local, projectPath);
                if (local.McpServers != null)
                    foreach (var key in local.McpServers.Keys)
                        patterns.Add($"mcp__{key}__*");
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Local MCP 서버 목록 읽기 실패");
            }
        }

        return patterns.Count > 0 ? patterns.ToList() : null;
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