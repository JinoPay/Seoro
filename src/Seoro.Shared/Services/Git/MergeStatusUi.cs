using MudBlazor;

namespace Seoro.Shared.Services.Git;

/// <summary>
///     머지 상태 UI에 사용하는 아이콘/CSS 클래스 헬퍼.
///     MergeToolbar / MergeStatusBanner / GitView 등에서 공유.
/// </summary>
public static class MergeStatusUi
{
    public static string StatusIcon(MergeStatusKind kind, int aheadCount = 0) => kind switch
    {
        MergeStatusKind.Clean when aheadCount == 0 => Icons.Material.Outlined.CheckCircle,
        MergeStatusKind.Clean => Icons.Material.Filled.CheckCircle,
        MergeStatusKind.BehindTarget => Icons.Material.Filled.Warning,
        MergeStatusKind.ConflictExpected => Icons.Material.Filled.Error,
        MergeStatusKind.UncommittedDirty => Icons.Material.Outlined.EditNote,
        MergeStatusKind.InConflict => Icons.Material.Filled.Warning,
        MergeStatusKind.NetworkError => Icons.Material.Filled.WifiOff,
        _ => Icons.Material.Outlined.HourglassEmpty
    };

    public static string StatusCssClass(MergeStatusKind kind) => kind switch
    {
        MergeStatusKind.Clean => "status-clean",
        MergeStatusKind.BehindTarget => "status-behind",
        MergeStatusKind.ConflictExpected => "status-conflict",
        MergeStatusKind.UncommittedDirty => "status-uncommitted",
        MergeStatusKind.InConflict => "status-in-conflict",
        MergeStatusKind.NetworkError => "status-offline",
        _ => "status-unknown"
    };
}
