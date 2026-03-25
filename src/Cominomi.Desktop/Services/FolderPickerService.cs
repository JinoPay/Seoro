using Cominomi.Shared.Services;

namespace Cominomi.Desktop.Services;

public class FolderPickerService : IFolderPickerService
{
    private readonly PhotinoWindowHolder _windowHolder;

    public FolderPickerService(PhotinoWindowHolder windowHolder)
    {
        _windowHolder = windowHolder;
    }

    public async Task<string?> PickFolderAsync()
    {
        var window = _windowHolder.Window;
        if (window == null) return null;

        var result = await window.ShowOpenFolderAsync("폴더 선택");
        return result?.FirstOrDefault();
    }
}
