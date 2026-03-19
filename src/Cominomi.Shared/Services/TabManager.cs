using Cominomi.Shared.Models;
using Cominomi.Shared.Models.ViewModels;

namespace Cominomi.Shared.Services;

public class TabManager
{
    public const long MaxSingleFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    public const long MaxTotalContentBytes = 50 * 1024 * 1024;   // 50 MB

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
            tab.LastAccessedAt = DateTime.UtcNow;
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
        var sizeBytes = (long)content.Length * 2; // UTF-16

        if (sizeBytes > MaxSingleFileSizeBytes)
        {
            var truncated = content[..(int)(MaxSingleFileSizeBytes / 2)];
            content = truncated + $"\n\n--- File truncated (original: {sizeBytes / 1024 / 1024} MB, limit: {MaxSingleFileSizeBytes / 1024 / 1024} MB) ---";
            sizeBytes = (long)content.Length * 2;
        }

        var existing = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.FileContent && t.FilePath == filePath);
        if (existing != null)
        {
            existing.FileContent = content;
            existing.ContentSizeBytes = sizeBytes;
            existing.ContentEvicted = false;
            existing.LastAccessedAt = DateTime.UtcNow;
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
                FileContent = content,
                ContentSizeBytes = sizeBytes,
                LastAccessedAt = DateTime.UtcNow
            };
            OpenTabs.Add(tab);
            ActiveTab = tab;
        }

        EvictLruContent();
        OnTabChanged?.Invoke();
    }

    public void OpenFileContentTab(string filePath)
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.FileContent && t.FilePath == filePath);
        if (existing != null)
        {
            existing.LastAccessedAt = DateTime.UtcNow;
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
                LastAccessedAt = DateTime.UtcNow
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
            existing.LastAccessedAt = DateTime.UtcNow;
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
                DiffData = diff,
                LastAccessedAt = DateTime.UtcNow
            };
            OpenTabs.Add(tab);
            ActiveTab = tab;
        }
        OnTabChanged?.Invoke();
    }

    public void OpenActivityTab()
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.Activity);
        if (existing != null)
        {
            ActiveTab = existing;
        }
        else
        {
            var tab = new MainTab { Id = "activity", Type = MainTabType.Activity, Title = "Activity" };
            OpenTabs.Add(tab);
            ActiveTab = tab;
        }
        OnTabChanged?.Invoke();
    }

    public void OpenNotificationsTab()
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.Notifications);
        if (existing != null)
        {
            ActiveTab = existing;
        }
        else
        {
            var tab = new MainTab { Id = "notifications", Type = MainTabType.Notifications, Title = "Notifications" };
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

    private void EvictLruContent()
    {
        var totalBytes = OpenTabs
            .Where(t => t.FileContent != null && !t.ContentEvicted)
            .Sum(t => t.ContentSizeBytes);

        if (totalBytes <= MaxTotalContentBytes) return;

        var candidates = OpenTabs
            .Where(t => t.Type == MainTabType.FileContent && t.FileContent != null && !t.ContentEvicted && t.Id != ActiveTab?.Id)
            .OrderBy(t => t.LastAccessedAt)
            .ToList();

        foreach (var tab in candidates)
        {
            if (totalBytes <= MaxTotalContentBytes) break;

            totalBytes -= tab.ContentSizeBytes;
            tab.FileContent = null;
            tab.ContentEvicted = true;
        }
    }
}
