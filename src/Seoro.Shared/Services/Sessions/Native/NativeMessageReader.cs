using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Seoro.Shared.Models.Chat;
using Seoro.Shared.Models.Sessions;

namespace Seoro.Shared.Services.Sessions.Native;

public interface INativeMessageReader
{
    Task<List<ChatMessage>> ReadAsync(Session session);
}

/// <summary>
///     세션의 메시지를 CLI 네이티브 jsonl(단일 진실 소스)에서 읽어옵니다.
///     - Claude: cwd → 프로젝트 해시 변환 후 ~/.claude/projects/&lt;hash&gt;/&lt;conversationId&gt;.jsonl
///     - Codex:  ~/.codex/sessions 하위에서 thread_id(=ConversationId)로 끝나는 rollout 파일 검색
///     파일을 찾지 못하거나 파싱에 실패해도 예외를 던지지 않고 빈 목록을 반환합니다.
/// </summary>
public partial class NativeMessageReader(ILogger<NativeMessageReader> logger) : INativeMessageReader
{
    private static readonly string ClaudeProjectsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    private static readonly string CodexSessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

    public Task<List<ChatMessage>> ReadAsync(Session session)
    {
        return Task.Run(() =>
        {
            var conversationId = session.ConversationId;
            if (string.IsNullOrEmpty(conversationId))
                return new List<ChatMessage>(); // 첫 턴 이전 — 네이티브 파일 없음

            try
            {
                var filePath = session.IsCodex
                    ? FindCodexFile(conversationId)
                    : FindClaudeFile(session.Git.WorktreePath, conversationId);

                if (filePath == null || !File.Exists(filePath))
                {
                    logger.LogDebug("세션 {SessionId}의 네이티브 파일을 찾을 수 없음 (conv={Conv})",
                        session.Id, conversationId);
                    return new List<ChatMessage>();
                }

                var messages = session.IsCodex
                    ? CodexRolloutParser.Parse(filePath)
                    : ClaudeNativeParser.Parse(filePath);

                foreach (var msg in messages)
                    msg.MigrateToParts();

                return messages;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "세션 {SessionId} 네이티브 메시지 로드 실패", session.Id);
                return new List<ChatMessage>();
            }
        });
    }

    private static string? FindClaudeFile(string cwd, string conversationId)
    {
        if (string.IsNullOrEmpty(cwd)) return null;
        var hash = PathToProjectHash(cwd);
        return Path.Combine(ClaudeProjectsDir, hash, $"{conversationId}.jsonl");
    }

    private string? FindCodexFile(string threadId)
    {
        if (!Directory.Exists(CodexSessionsDir)) return null;

        try
        {
            // rollout-<timestamp>-<threadId>.jsonl
            return Directory
                .EnumerateFiles(CodexSessionsDir, $"*{threadId}.jsonl", SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Codex 세션 디렉토리 검색 실패: {ThreadId}", threadId);
            return null;
        }
    }

    /// <summary>
    ///     cwd 경로를 Claude의 프로젝트 디렉토리 이름으로 변환합니다.
    ///     Claude는 영숫자가 아닌 모든 문자를 '-'로 치환합니다.
    ///     예: /Users/me/Projects/Seoro → -Users-me-Projects-Seoro, net10.0 → net10-0
    ///     <see cref="SessionReplayService.ProjectHashToPath" />의 역함수입니다.
    /// </summary>
    internal static string PathToProjectHash(string cwd)
    {
        return NonAlphaNumeric().Replace(cwd, "-");
    }

    [GeneratedRegex("[^a-zA-Z0-9]")]
    private static partial Regex NonAlphaNumeric();
}
