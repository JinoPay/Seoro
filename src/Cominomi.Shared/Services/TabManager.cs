using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class TabManager
{
    public List<MainTab> OpenTabs { get; private set; } = new();
    public MainTab? ActiveTab { get; private set; }

    public event Action? OnTabChanged;

    public void Reset(string? sessionTitle = null)
    {
        OpenTabs.Clear();
        ActiveTab = null;
        EnsureChatTab(sessionTitle);
    }

    public void EnsureChatTab(string? sessionTitle = null)
    {
        if (!OpenTabs.Any(t => t.Type == MainTabType.Chat))
        {
            var chatTab = new MainTab
            {
                Id = "chat",
                Type = MainTabType.Chat,
                Title = sessionTitle ?? "Chat"
            };
            OpenTabs.Insert(0, chatTab);
        }
        if (ActiveTab == null)
        {
            ActiveTab = OpenTabs.First(t => t.Type == MainTabType.Chat);
        }
    }

    public void SetActiveTab(string tabId)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Id == tabId);
        if (tab != null)
        {
            ActiveTab = tab;
            OnTabChanged?.Invoke();
        }
    }

    public void CloseTab(string tabId)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null || tab.Type == MainTabType.Chat) return;

        var idx = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);

        if (ActiveTab?.Id == tabId)
        {
            ActiveTab = OpenTabs.ElementAtOrDefault(Math.Min(idx, OpenTabs.Count - 1))
                        ?? OpenTabs.FirstOrDefault();
        }
        OnTabChanged?.Invoke();
    }

    public void OpenFileContentTab(string filePath, string content)
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.FileContent && t.FilePath == filePath);
        if (existing != null)
        {
            existing.FileContent = content;
            ActiveTab = existing;
        }
        else
        {
            var fileName = Path.GetFileName(filePath);
            var tab = new MainTab
            {
                Type = MainTabType.FileContent,
                Title = fileName,
                FilePath = filePath,
                FileContent = content
            };
            OpenTabs.Add(tab);
            ActiveTab = tab;
        }
        OnTabChanged?.Invoke();
    }

    public void OpenFileContentTab(string filePath)
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.FileContent && t.FilePath == filePath);
        if (existing != null)
        {
            ActiveTab = existing;
        }
        else
        {
            var fileName = Path.GetFileName(filePath);
            var tab = new MainTab
            {
                Type = MainTabType.FileContent,
                Title = fileName,
                FilePath = filePath
            };
            OpenTabs.Add(tab);
            ActiveTab = tab;
        }
        OnTabChanged?.Invoke();
    }

    public void OpenFileDiffTab(string filePath, FileDiff diff)
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.FileDiff && t.FilePath == filePath);
        if (existing != null)
        {
            existing.DiffData = diff;
            ActiveTab = existing;
        }
        else
        {
            var fileName = Path.GetFileName(filePath);
            var tab = new MainTab
            {
                Type = MainTabType.FileDiff,
                Title = fileName,
                FilePath = filePath,
                DiffData = diff
            };
            OpenTabs.Add(tab);
            ActiveTab = tab;
        }
        OnTabChanged?.Invoke();
    }

    public void UpdateChatTabTitle(string title)
    {
        var chatTab = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.Chat);
        if (chatTab != null) chatTab.Title = title;
        OnTabChanged?.Invoke();
    }
}
