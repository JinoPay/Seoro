using Microsoft.AspNetCore.Components;

namespace Seoro.Shared.Services.Ui;

/// <summary>
/// 모달 다이얼로그 서비스 (MudBlazor IDialogService 대체).
/// 이름 충돌 방지를 위해 IDialogService가 아닌 IModalService로 명명한다.
/// </summary>
public interface IModalService
{
    /// <summary>컴포넌트를 모달로 띄운다. 결과는 ModalReference.Result로 대기.</summary>
    ModalReference Show<TComponent>(string? title = null, ModalParameters? parameters = null,
        ModalOptions? options = null) where TComponent : IComponent;

    /// <summary>
    /// 메시지 박스. 반환: yes=true, no=false, cancel/dismiss=null (3상태, Mud 호환).
    /// </summary>
    Task<bool?> ShowMessageBoxAsync(string title, string message, string yesText,
        string? noText = null, string? cancelText = null);
}

/// <summary>모달 내부 컴포넌트에 캐스케이드되는 인스턴스 핸들. 닫기/취소에 사용.</summary>
public interface IModalInstance
{
    void Close(ModalResult result);
    void Close();
    void Cancel();
}

public sealed class ModalResult
{
    private ModalResult(bool canceled, object? data)
    {
        Canceled = canceled;
        Data = data;
    }

    public bool Canceled { get; }
    public object? Data { get; }

    public static ModalResult Ok(object? data = null) => new(false, data);
    public static ModalResult Cancel() => new(true, null);

    public T? As<T>() => Data is T typed ? typed : default;
}

public sealed class ModalReference
{
    private readonly TaskCompletionSource<ModalResult> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ModalResult> Result => _tcs.Task;

    internal bool TrySetResult(ModalResult result) => _tcs.TrySetResult(result);
}

public sealed class ModalParameters : Dictionary<string, object?>;

public sealed class ModalOptions
{
    /// <summary>모달 패널 max-width CSS 값 (예: "480px").</summary>
    public string? MaxWidth { get; set; }

    public bool CloseOnEscape { get; set; } = true;
    public bool CloseOnBackdropClick { get; set; }
    public bool ShowCloseButton { get; set; } = true;
}
