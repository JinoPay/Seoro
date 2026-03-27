using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface ISessionReplayService
{
    Task<List<SessionReplaySummary>> ListSessionsAsync();
    Task<List<SessionReplayEvent>> LoadEventsAsync(string filePath, int skip = 0, int take = 100);
}
