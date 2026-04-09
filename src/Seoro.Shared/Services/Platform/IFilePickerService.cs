
namespace Seoro.Shared.Services.Platform;

public interface IFilePickerService
{
    Task<List<PendingAttachment>?> PickFilesAsync();
}