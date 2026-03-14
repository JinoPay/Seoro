using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IClaudeService
{
    IAsyncEnumerable<StreamEvent> SendMessageAsync(string message, string workingDir, string model, string permissionMode = "default", CancellationToken ct = default);
    void Cancel();
}
