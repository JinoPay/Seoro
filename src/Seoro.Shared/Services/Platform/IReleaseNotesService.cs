
namespace Seoro.Shared.Services.Platform;

public interface IReleaseNotesService
{
    Task<IReadOnlyList<ReleaseNote>> GetReleaseNotesAsync();
}