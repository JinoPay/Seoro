using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IReleaseNotesService
{
    Task<IReadOnlyList<ReleaseNote>> GetReleaseNotesAsync();
}