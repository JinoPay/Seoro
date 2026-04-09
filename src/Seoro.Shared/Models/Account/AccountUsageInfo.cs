namespace Seoro.Shared.Models.Account;

public class UsageBucket
{
    public double Utilization { get; set; }       // 0-100
    public DateTimeOffset? ResetsAt { get; set; }
}

public class AccountUsageInfo
{
    public string AccountId { get; set; } = "";
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    // ── Usage buckets matching the API response ───────────────────────────
    public UsageBucket? FiveHour { get; set; }
    public UsageBucket? SevenDayAll { get; set; }
    public UsageBucket? SevenDaySonnet { get; set; }

    // ── Raw API response (for debugging / forward-compat) ─────────────────
    public string? RawResponseJson { get; set; }
}
