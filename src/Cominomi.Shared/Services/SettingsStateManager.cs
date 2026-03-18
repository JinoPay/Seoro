namespace Cominomi.Shared.Services;

public class SettingsStateManager
{
    private readonly Action _notifyChanged;

    public SettingsStateManager(Action notifyChanged)
    {
        _notifyChanged = notifyChanged;
    }

    public bool ShowSettings { get; private set; }
    public string SettingsSection { get; private set; } = "general";
    public string? SettingsWorkspaceId { get; private set; }

    public void OpenSettings(string section = "general", string? workspaceId = null)
    {
        ShowSettings = true;
        SettingsSection = section;
        SettingsWorkspaceId = workspaceId;
        _notifyChanged();
    }

    public void CloseSettings()
    {
        ShowSettings = false;
        SettingsWorkspaceId = null;
        _notifyChanged();
    }

    public void SetSettingsSection(string section)
    {
        SettingsSection = section;
        _notifyChanged();
    }

    public void SetSettingsWorkspace(string? workspaceId)
    {
        SettingsWorkspaceId = workspaceId;
        SettingsSection = workspaceId != null ? "ws-general" : "general";
        _notifyChanged();
    }
}
