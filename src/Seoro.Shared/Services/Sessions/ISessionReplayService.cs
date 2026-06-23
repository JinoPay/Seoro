
namespace Seoro.Shared.Services.Sessions;

public interface ISessionReplayService
{
    Task RefreshSessionIndexAsync(bool force = false);
    Task SetTagAsync(string sessionId, List<string> tags, string? note = null);
    Task<List<LiveSessionInfo>> DetectLiveSessionsAsync();
    Task<List<SessionSearchResult>> SearchAsync(string query, int maxResults = 20);
    Task<SessionListResult> ListSessionsAsync(int limit = 10, int offset = 0);
    Task<SessionLoadResult> LoadSessionAsync(string filePath, int limit = 50, int offset = 0);
    Task<SessionTagsData> GetTagsAsync();
    Task<string> ExportToMarkdownAsync(string filePath);
}