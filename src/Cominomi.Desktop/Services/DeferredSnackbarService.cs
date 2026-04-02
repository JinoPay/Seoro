using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace Cominomi.Desktop.Services;

/// <summary>
///     Lazily creates SnackbarService to avoid NavigationManager.AssertInitialized()
///     crash during Blazor Hybrid startup.
/// </summary>
public sealed class DeferredSnackbarService(IServiceProvider sp) : ISnackbar
{
    private SnackbarService? _inner;

    private SnackbarService Inner => _inner ??= new SnackbarService(
        sp.GetRequiredService<NavigationManager>(),
        sp.GetRequiredService<TimeProvider>(),
        sp.GetRequiredService<IOptions<SnackbarConfiguration>>());

    public void Dispose()
    {
        _inner?.Dispose();
        _inner = null;
    }

    public event Action? OnSnackbarsUpdated
    {
        add => Inner.OnSnackbarsUpdated += value;
        remove => Inner.OnSnackbarsUpdated -= value;
    }

    public IEnumerable<Snackbar> ShownSnackbars => Inner.ShownSnackbars;

    public Snackbar? Add(string message, Severity severity = Severity.Normal,
        Action<SnackbarOptions>? configure = null, string? key = null)
    {
        return Inner.Add(message, severity, configure, key);
    }

    public Snackbar? Add(MarkupString message, Severity severity = Severity.Normal,
        Action<SnackbarOptions>? configure = null, string? key = null)
    {
        return Inner.Add(message, severity, configure, key);
    }

    public Snackbar? Add(RenderFragment message, Severity severity = Severity.Normal,
        Action<SnackbarOptions>? configure = null, string? key = null)
    {
        return Inner.Add(message, severity, configure, key);
    }

    public Snackbar? Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        Dictionary<string, object>? componentParameters = null, Severity severity = Severity.Normal,
        Action<SnackbarOptions>? configure = null, string? key = null) where T : IComponent
    {
        return Inner.Add<T>(componentParameters, severity, configure, key);
    }

    public SnackbarConfiguration Configuration => Inner.Configuration;

    public void Clear()
    {
        Inner.Clear();
    }

    public void Remove(Snackbar snackbar)
    {
        Inner.Remove(snackbar);
    }

    public void RemoveByKey(string key)
    {
        Inner.RemoveByKey(key);
    }
}