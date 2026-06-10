namespace Seoro.Shared.UiKit;

/// <summary>색상 톤 (MudBlazor Color/Severity 대체).</summary>
public enum Tone
{
    Default,
    Primary,
    Secondary,
    Success,
    Warning,
    Error,
    Info,
}

/// <summary>컴포넌트 크기 (MudBlazor Size 대체). 기본 Sm = 컴팩트.</summary>
public enum UiSize
{
    Xs,
    Sm,
    Md,
}

/// <summary>토스트 심각도 (MudBlazor Severity 대체). Normal은 중립(아이콘 없음).</summary>
public enum ToastSeverity
{
    Normal,
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>버튼 스타일 (MudBlazor Variant 대체).</summary>
public enum ButtonStyle
{
    Filled,
    Outlined,
    Text,
    Subtle,
}

/// <summary>툴팁/팝오버 배치 방향.</summary>
public enum UiPlacement
{
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>팝오버 앵커 기준 위치 (MudBlazor AnchorOrigin/TransformOrigin 조합 대체).</summary>
public enum PopoverPlacement
{
    BottomStart,
    BottomEnd,
    TopStart,
    TopEnd,
}
