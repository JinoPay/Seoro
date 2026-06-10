using Seoro.Shared.UiKit;

namespace Seoro.Shared.Services.Git;

/// <summary>
///     머지 상태 UI에 사용하는 아이콘/CSS 클래스 헬퍼.
///     MergeToolbar / MergeStatusBanner / GitView 등에서 공유.
/// </summary>
public static class MergeStatusUi
{
    public static string StatusIcon(MergeStatusKind kind, int aheadCount = 0) => kind switch
    {
        MergeStatusKind.Clean => Lucide.CircleCheck,
        MergeStatusKind.BehindTarget => Lucide.TriangleAlert,
        MergeStatusKind.ConflictExpected => Lucide.CircleAlert,
        MergeStatusKind.UncommittedDirty => Lucide.SquarePen,
        MergeStatusKind.InConflict => Lucide.TriangleAlert,
        MergeStatusKind.NetworkError => Lucide.WifiOff,
        _ => Lucide.Hourglass
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
