using Microsoft.AspNetCore.Components;

namespace Seoro.Shared.Services.Ui;

/// <summary>
/// IModalService 구현. 모달 스택을 보관하고 ModalHost가 구독해 렌더링한다.
/// EventBus 등 렌더 루프 밖에서 호출되어도 안전하다 (호스트가 InvokeAsync로 마샬링).
/// </summary>
public sealed class ModalService : IModalService
{
    private readonly object _lock = new();
    private readonly List<ModalItem> _stack = [];

    /// <summary>ModalHost가 구독. 스택 변경 시 발행.</summary>
    public event Action? OnModalsChanged;

    public IReadOnlyList<ModalItem> Modals
    {
        get
        {
            lock (_lock)
            {
                return _stack.ToList();
            }
        }
    }

    public ModalReference Show<TComponent>(string? title = null, ModalParameters? parameters = null,
        ModalOptions? options = null) where TComponent : IComponent
    {
        var reference = new ModalReference();
        var item = new ModalItem
        {
            Title = title,
            Options = options ?? new ModalOptions(),
            Reference = reference,
            ComponentType = typeof(TComponent),
            Parameters = parameters,
        };

        Push(item);
        return reference;
    }

    public async Task<bool?> ShowMessageBoxAsync(string title, string message, string yesText,
        string? noText = null, string? cancelText = null)
    {
        var reference = new ModalReference();
        var item = new ModalItem
        {
            Title = title,
            Options = new ModalOptions { MaxWidth = "420px", ShowCloseButton = false },
            Reference = reference,
            MessageBox = new MessageBoxSpec(message, yesText, noText, cancelText),
        };

        Push(item);
        var result = await reference.Result;
        // yes=Ok(true), no=Ok(false), cancel/dismiss=Canceled
        return result.Canceled ? null : result.As<bool?>();
    }

    /// <summary>모달을 닫고 결과를 설정한다 (ModalHost에서 호출).</summary>
    public void Close(ModalItem item, ModalResult result)
    {
        lock (_lock)
        {
            if (!_stack.Remove(item))
                return;
        }

        item.Reference.TrySetResult(result);
        OnModalsChanged?.Invoke();
    }

    private void Push(ModalItem item)
    {
        lock (_lock)
        {
            _stack.Add(item);
        }

        OnModalsChanged?.Invoke();
    }
}

/// <summary>모달 스택 항목. ComponentType 또는 MessageBox 중 하나가 채워진다.</summary>
public sealed class ModalItem
{
    public Guid Id { get; } = Guid.NewGuid();
    public string? Title { get; init; }
    public required ModalOptions Options { get; init; }
    public required ModalReference Reference { get; init; }
    public Type? ComponentType { get; init; }
    public ModalParameters? Parameters { get; init; }
    public MessageBoxSpec? MessageBox { get; init; }
}

public sealed record MessageBoxSpec(string Message, string YesText, string? NoText, string? CancelText);
