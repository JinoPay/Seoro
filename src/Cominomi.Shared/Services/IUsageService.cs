using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IUsageService
{
    Task RecordUsageAsync(UsageEntry entry);
    Task<UsageStats> GetStatsAsync(int? days = null);
    Task<UsageStats> GetStatsByDateRangeAsync(DateTime start, DateTime end);
    decimal CalculateCost(string model, long inputTokens, long outputTokens, long cacheCreationTokens, long cacheReadTokens);
}
