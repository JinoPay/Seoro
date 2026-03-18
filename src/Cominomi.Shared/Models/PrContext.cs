namespace Cominomi.Shared.Models;

/// <summary>
/// PR/이슈 관련 속성을 그룹화한 모델.
/// Session에서 분리하여 관심사를 명확히 구분합니다.
/// </summary>
public class PrContext
{
    public string? PrUrl { get; set; }
    public int? PrNumber { get; set; }
    public int? IssueNumber { get; set; }
    public string? IssueUrl { get; set; }
    public List<string>? ConflictFiles { get; set; }
}
