using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IFilePickerService
{
    Task<List<PendingAttachment>?> PickFilesAsync();
}
