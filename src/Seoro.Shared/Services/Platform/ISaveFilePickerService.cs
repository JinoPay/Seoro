namespace Seoro.Shared.Services.Platform;

public interface ISaveFilePickerService
{
    /// <summary>네이티브 "다른 이름으로 저장" 다이얼로그. 취소 시 null 반환.</summary>
    Task<string?> PickSaveFileAsync(string title, string defaultFileName, string filterName, string filterExtension);

    /// <summary>네이티브 "파일 열기" 다이얼로그 (단일 파일). 취소 시 null 반환.</summary>
    Task<string?> PickOpenFileAsync(string title, string filterName, string filterExtension);
}
