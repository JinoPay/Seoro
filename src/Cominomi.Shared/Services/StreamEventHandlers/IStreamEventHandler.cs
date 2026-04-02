using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services.StreamEventHandlers;

/// <summary>
///     Handles a specific type of stream event from the Claude CLI.
///     Implementations are registered via DI and dispatched by <see cref="StreamEventProcessor" />.
/// </summary>
public interface IStreamEventHandler
{
    /// <summary>
    ///     The primary event type this handler processes (e.g. "content_block_start", "result").
    /// </summary>
    string EventType { get; }

    Task HandleAsync(StreamEvent evt, StreamProcessingContext ctx);
}