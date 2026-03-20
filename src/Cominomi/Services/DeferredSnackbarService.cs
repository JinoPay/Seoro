using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace Cominomi.Services;

/// <summary>
/// Lazily creates SnackbarService to avoid NavigationManager.AssertInitialized()
/// crash during MAUI Blazor Hybrid startup.
/// </summary>
public sealed class DeferredSnackbarService : ISnackbar
{
    private readonly IServiceProvider _sp;
    private SnackbarService? _inner;

    public DeferredSnackbarService(IServiceProvider sp)
    {
        _sp = sp;
    }

    private SnackbarService Inner => _inner ??= new SnackbarService(
        _sp.GetRequiredService<NavigationManager>(),
        _sp.GetRequiredService<TimeProvider>(),
        _sp.GetRequiredService<IOptions<SnackbarConfiguration>>());

    public IEnumerable<Snackbar> ShownSnackbars => Inner.ShownSnackbars;
    public SnackbarConfiguration Configuration => Inner.Configuration;

    public event Action? OnSnackbarsUpdated
    {
        add => Inner.OnSnackbarsUpdated += value;
        remove => Inner.OnSnackbarsUpdated -= value;
    }

    public Snackbar? Add(string message, Severity severity = Severity.Normal,
        Action<SnackbarOptions>? configure = null, string? key = null)
        => Inner.Add(message, severity, configure, key);

    public Snackbar? Add(MarkupString message, Severity severity = Severity.Normal,
        Action<SnackbarOptions>? configure = null, string? key = null)
        => Inner.Add(message, severity, configure, key);

    public Snackbar? Add(RenderFragment message, Severity severity = Severity.Normal,
        Action<SnackbarOptions>? configure = null, string? key = null)
        => Inner.Add(message, severity, configure, key);

    public Snackbar? Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        Dictionary<string, object>? componentParameters = null, Severity severity = Severity.Normal,
        Action<SnackbarOptions>? configure = null, string? key = null) where T : IComponent
        => Inner.Add<T>(componentParameters, severity, configure, key);

    public void Clear() => Inner.Clear();
    public void Remove(Snackbar snackbar) => Inner.Remove(snackbar);
    public void RemoveByKey(string key) => Inner.RemoveByKey(key);

    public void Dispose()
    {
        _inner?.Dispose();
        _inner = null;
    }
}
