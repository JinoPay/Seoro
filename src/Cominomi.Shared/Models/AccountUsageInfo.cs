namespace Cominomi.Shared.Models;

public class AccountUsageInfo
{
    public string AccountId { get; set; } = "";
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    // ── 5-hour rate limit window ──────────────────────────────────────────
    public double? Utilization { get; set; }   // 0-100 %
    public double? Limit { get; set; }
    public double? Used { get; set; }

    // ── Current week (all models) ─────────────────────────────────────────
    public long? WeeklyAllTokens { get; set; }
    public long? WeeklyAllRequests { get; set; }

    // ── Current week (Sonnet only) ────────────────────────────────────────
    public long? WeeklySonnetTokens { get; set; }
    public long? WeeklySonnetRequests { get; set; }

    // ── Raw API response (for debugging / forward-compat) ─────────────────
    public string? RawResponseJson { get; set; }
}
