namespace Cominomi.Shared.Services;

public class FilePreviewService
{
    public bool IsOpen { get; private set; }
    public string Content { get; private set; } = "";
    public string FileName { get; private set; } = "";

    public event Action? OnChange;

    public void Close()
    {
        IsOpen = false;
        OnChange?.Invoke();
    }

    public void Open(string fileName, string content)
    {
        FileName = fileName;
        Content = content;
        IsOpen = true;
        OnChange?.Invoke();
    }
}