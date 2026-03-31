using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISessionReplayService
{
    Task RefreshSessionIndexAsync(bool force = false);
    Task<SessionListResult> ListSessionsAsync(int limit = 10, int offset = 0);
    Task<SessionLoadResult> LoadSessionAsync(string filePath, int limit = 50, int offset = 0);
    Task<List<SessionSearchResult>> SearchAsync(string query, int maxResults = 20);
    Task<List<SessionReplaySummary>> SearchIndexAsync(string query, int maxResults = 20);
    Task<List<LiveSessionInfo>> DetectLiveSessionsAsync();
    Task<SessionTagsData> GetTagsAsync();
    Task SetTagAsync(string sessionId, List<string> tags, string? note = null);
    Task<string> ExportToMarkdownAsync(string filePath);
}
