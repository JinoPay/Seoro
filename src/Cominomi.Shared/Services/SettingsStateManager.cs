namespace Cominomi.Shared.Services;

public class SettingsStateManager(Action notifyChanged)
{
    public bool ShowSettings { get; private set; }
    public string SettingsSection { get; private set; } = "general";
    public string? SettingsWorkspaceId { get; private set; }

    public void CloseSettings()
    {
        ShowSettings = false;
        SettingsWorkspaceId = null;
        notifyChanged();
    }

    public void OpenSettings(string section = "general", string? workspaceId = null)
    {
        ShowSettings = true;
        SettingsSection = section;
        SettingsWorkspaceId = workspaceId;
        notifyChanged();
    }

    public void SetSettingsSection(string section)
    {
        SettingsSection = section;
        notifyChanged();
    }

    public void SetSettingsWorkspace(string? workspaceId)
    {
        SettingsWorkspaceId = workspaceId;
        SettingsSection = workspaceId != null ? "ws-general" : "general";
        notifyChanged();
    }
}