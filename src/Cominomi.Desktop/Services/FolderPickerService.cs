using Cominomi.Shared.Services;

namespace Cominomi.Desktop.Services;

public class FolderPickerService(PhotinoWindowHolder windowHolder) : IFolderPickerService
{
    public async Task<string?> PickFolderAsync()
    {
        var window = windowHolder.Window;
        if (window == null) return null;

        var result = await window.ShowOpenFolderAsync("폴더 선택");
        return result?.FirstOrDefault();
    }
}