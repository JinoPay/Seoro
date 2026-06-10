using Seoro.Shared.UiKit;

namespace Seoro.Shared.Services.Ui;

/// <summary>
/// 토스트 알림 서비스 (MudBlazor ISnackbar 대체).
/// 호출 시그니처를 ISnackbar.Add와 동일하게 유지해 마이그레이션을 기계적으로 만든다.
/// </summary>
public interface IToastService
{
    event Action? OnToastsUpdated;

    IReadOnlyList<ToastInstance> Toasts { get; }

    ToastInstance? Add(string message, ToastSeverity severity = ToastSeverity.Normal,
        Action<ToastOptions>? configure = null);

    void Remove(ToastInstance toast);

    void Clear();
}

public sealed class ToastOptions
{
    /// <summary>표시 지속 시간(ms).</summary>
    public int VisibleStateDuration { get; set; } = 3000;

    /// <summary>액션 버튼 텍스트 (null이면 버튼 없음).</summary>
    public string? Action { get; set; }

    /// <summary>액션 버튼 클릭 핸들러.</summary>
    public Func<ToastInstance, Task>? OnClick { get; set; }
}

public sealed class ToastInstance
{
    public Guid Id { get; } = Guid.NewGuid();
    public required string Message { get; init; }
    public required ToastSeverity Severity { get; init; }
    public required ToastOptions Options { get; init; }
}
