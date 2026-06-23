namespace Seoro.Shared.Models.Common;

/// <summary>
///     일별 활동 집계 항목. 통계(Statistics)·게임화(Gamification) 양 도메인이 공유하므로 Common에 둔다.
/// </summary>
public class DailyActivityEntry
{
    public int MessageCount { get; set; }
    public int SessionCount { get; set; }
    public int ToolCallCount { get; set; }
    public string Date { get; set; } = "";
}
