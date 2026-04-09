namespace Seoro.Shared.Services.Platform;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync();
}