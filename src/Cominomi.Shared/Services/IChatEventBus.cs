namespace Cominomi.Shared.Services;

/// <summary>
///     Lightweight publish/subscribe bus that replaces the monolithic ChatState.OnChange.
///     Services publish typed events; UI components subscribe to only the events they need.
/// </summary>
public interface IChatEventBus
{
    /// <summary>
    ///     Legacy bridge: fires on every event for components that haven't migrated
    ///     to typed subscriptions yet. Equivalent to the old ChatState.OnChange.
    /// </summary>
    event Action? OnAny;

    IDisposable Subscribe<T>(Action<T> handler) where T : ChatEvent;
    void Publish<T>(T evt) where T : ChatEvent;
}