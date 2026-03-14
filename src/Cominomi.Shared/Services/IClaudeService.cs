using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IClaudeService
{
    IAsyncEnumerable<StreamEvent> SendMessageAsync(string message, string workingDir, string model, CancellationToken ct = default);
    void Cancel();
}
