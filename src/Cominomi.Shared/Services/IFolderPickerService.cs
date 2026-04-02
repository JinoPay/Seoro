namespace Cominomi.Shared.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync();
}