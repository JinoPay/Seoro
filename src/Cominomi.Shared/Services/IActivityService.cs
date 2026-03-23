using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IActivityService
{
    Task<ActionTimelineResult> GetActionTimelineAsync(ActionTimelineFilter filter, CancellationToken ct = default);
}
