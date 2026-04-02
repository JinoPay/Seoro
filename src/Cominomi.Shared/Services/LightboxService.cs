namespace Cominomi.Shared.Services;

public class LightboxService
{
    public bool IsOpen { get; private set; }
    public string AltText { get; private set; } = "";
    public string ImageSrc { get; private set; } = "";

    public event Action? OnChange;

    public void Close()
    {
        IsOpen = false;
        OnChange?.Invoke();
    }

    public void Open(string imageSrc, string altText = "")
    {
        ImageSrc = imageSrc;
        AltText = altText;
        IsOpen = true;
        OnChange?.Invoke();
    }
}