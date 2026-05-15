using Seoro.Shared.Models.ViewModels;

namespace Seoro.Shared.Services.Chat;

public class TabManager
{
    public const long MaxSingleFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    public const long MaxTotalContentBytes = 50 * 1024 * 1024; // 50 MB

    public List<MainTab> OpenTabs { get; } = [];
    public MainTab? ActiveTab { get; private set; }

    public event Action? OnTabChanged;

    public void CloseTab(string tabId)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null || tab.Type == MainTabType.Chat) return;

        var idx = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);

        if (ActiveTab?.Id == tabId)
            ActiveTab = OpenTabs.ElementAtOrDefault(Math.Min(idx, OpenTabs.Count - 1))
                        ?? OpenTabs.FirstOrDefault();
        RecalculateDisambiguatedTitles();
        OnTabChanged?.Invoke();
    }

    public void CloseOtherTabs(string tabId)
    {
        var target = OpenTabs.FirstOrDefault(t => t.Id == tabId);
        if (target == null) return;

        OpenTabs.RemoveAll(t => t.Type != MainTabType.Chat && t.Id != tabId);
        ActiveTab = target;
        RecalculateDisambiguatedTitles();
        OnTabChanged?.Invoke();
    }

    public void CloseTabsToTheRight(string tabId)
    {
        var idx = OpenTabs.FindIndex(t => t.Id == tabId);
        if (idx < 0) return;

        var toRemove = OpenTabs.Skip(idx + 1).ToList();
        foreach (var tab in toRemove)
            OpenTabs.Remove(tab);

        if (ActiveTab != null && toRemove.Contains(ActiveTab))
            ActiveTab = OpenTabs.ElementAtOrDefault(idx) ?? OpenTabs.FirstOrDefault();

        RecalculateDisambiguatedTitles();
        OnTabChanged?.Invoke();
    }

    public void CloseAllFileTabs()
    {
        OpenTabs.RemoveAll(t => t.Type != MainTabType.Chat);
        ActiveTab = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.Chat);
        RecalculateDisambiguatedTitles();
        OnTabChanged?.Invoke();
    }

    public void EnsureChatTab(string? sessionTitle = null)
    {
        if (OpenTabs.All(t => t.Type != MainTabType.Chat))
        {
            var chatTab = new MainTab
            {
                Id = "chat",
                Type = MainTabType.Chat,
                Title = sessionTitle ?? "Chat"
            };
            OpenTabs.Insert(0, chatTab);
        }

        if (ActiveTab == null) ActiveTab = OpenTabs.First(t => t.Type == MainTabType.Chat);
    }

    public void OpenFileContentTab(string filePath, string content, DateTime? mtimeUtc = null)
    {
        var sizeBytes = (long)content.Length * 2; // UTF-16

        if (sizeBytes > MaxSingleFileSizeBytes)
        {
            var truncated = content[..(int)(MaxSingleFileSizeBytes / 2)];
            content = truncated +
                      $"\n\n--- File truncated (original: {sizeBytes / 1024 / 1024} MB, limit: {MaxSingleFileSizeBytes / 1024 / 1024} MB) ---";
            sizeBytes = (long)content.Length * 2;
        }

        var existing = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.FileContent && t.FilePath == filePath);
        if (existing != null)
        {
            existing.FileContent = content;
            existing.ContentSizeBytes = sizeBytes;
            existing.ContentEvicted = false;
            existing.LastAccessedAt = DateTime.UtcNow;
            existing.OriginalContent = content;
            existing.IsDirty = false;
            existing.LastLoadedMtimeUtc = mtimeUtc;
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
                LastAccessedAt = DateTime.UtcNow,
                OriginalContent = content,
                IsDirty = false,
                LastLoadedMtimeUtc = mtimeUtc
            };
            OpenTabs.Add(tab);
            ActiveTab = tab;
        }

        EvictLruContent();
        RecalculateDisambiguatedTitles();
        OnTabChanged?.Invoke();
    }

    public void MarkDirty(string filePath)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.FileContent && t.FilePath == filePath);
        if (tab == null || tab.IsDirty) return;
        tab.IsDirty = true;
        OnTabChanged?.Invoke();
    }

    public void MarkSaved(string filePath, string savedContent, DateTime mtimeUtc)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.FileContent && t.FilePath == filePath);
        if (tab == null) return;
        tab.FileContent = savedContent;
        tab.OriginalContent = savedContent;
        tab.IsDirty = false;
        tab.LastSavedAt = DateTime.UtcNow;
        tab.LastLoadedMtimeUtc = mtimeUtc;
        tab.ContentSizeBytes = (long)savedContent.Length * 2;
        OnTabChanged?.Invoke();
    }

    public void UpdateLoadedMtime(string filePath, DateTime mtimeUtc)
    {
        var tab = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.FileContent && t.FilePath == filePath);
        if (tab == null) return;
        tab.LastLoadedMtimeUtc = mtimeUtc;
    }

    public bool HasAnyDirty() => OpenTabs.Any(t => t.IsDirty);

    public IReadOnlyList<MainTab> GetDirtyTabs() => OpenTabs.Where(t => t.IsDirty).ToList();

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

        RecalculateDisambiguatedTitles();
        OnTabChanged?.Invoke();
    }

    public void OpenFileDiffTab(string filePath, FileDiff diff, string? commitSha = null)
    {
        var existing = OpenTabs.FirstOrDefault(t =>
            t.Type == MainTabType.FileDiff && t.FilePath == filePath && t.CommitSha == commitSha);
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
                CommitSha = commitSha,
                LastAccessedAt = DateTime.UtcNow
            };
            OpenTabs.Add(tab);
            ActiveTab = tab;
        }

        RecalculateDisambiguatedTitles();
        OnTabChanged?.Invoke();
    }

    public MainTab OpenGitGraphTab()
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Type == MainTabType.GitGraph);
        if (existing != null)
        {
            existing.LastAccessedAt = DateTime.UtcNow;
            ActiveTab = existing;
        }
        else
        {
            existing = new MainTab
            {
                Type = MainTabType.GitGraph,
                Title = "Git Graph",
                LastAccessedAt = DateTime.UtcNow
            };
            OpenTabs.Add(existing);
            ActiveTab = existing;
        }

        OnTabChanged?.Invoke();
        return existing;
    }

    public void Reset(string? sessionTitle = null)
    {
        OpenTabs.Clear();
        ActiveTab = null;
        EnsureChatTab(sessionTitle);
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
            .Where(t => t.Type == MainTabType.FileContent && t.FileContent != null && !t.ContentEvicted &&
                        !t.IsDirty && t.Id != ActiveTab?.Id)
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

    private void RecalculateDisambiguatedTitles()
    {
        var fileTabs = OpenTabs.Where(t => t.Type is MainTabType.FileContent or MainTabType.FileDiff).ToList();

        // Reset all
        foreach (var tab in fileTabs)
            tab.DisambiguatedTitle = null;

        // Group by Title (filename) to find collisions
        var groups = fileTabs.GroupBy(t => t.Title).Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var tabs = group.ToList();

            // 1) 같은 path + 다른 commit sha (또는 working tree vs commit) — sha 접두로 구분.
            var byPath = tabs.GroupBy(t => t.FilePath).Where(g => g.Count() > 1).ToList();
            foreach (var pathGroup in byPath)
            {
                var inPath = pathGroup.ToList();
                if (inPath.Any(t => t.CommitSha != null))
                {
                    foreach (var tab in inPath)
                    {
                        var prefix = tab.CommitSha is { Length: > 0 } sha
                            ? $"[{sha[..Math.Min(7, sha.Length)]}]"
                            : "[diff]";
                        tab.DisambiguatedTitle = $"{prefix} {tab.Title}";
                    }
                }
                else
                {
                    foreach (var tab in inPath)
                        if (tab.Type == MainTabType.FileDiff)
                            tab.DisambiguatedTitle = $"[diff] {tab.Title}";
                }
            }

            // 2) 같은 title + 다른 path — 부모 디렉터리 접두
            var stillColliding = tabs
                .Where(t => t.DisambiguatedTitle == null)
                .GroupBy(t => t.Title)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g);

            foreach (var tab in stillColliding)
            {
                if (tab.FilePath == null) continue;
                var dir = Path.GetDirectoryName(tab.FilePath)?.Replace('\\', '/');
                var lastDir = dir?.Split('/').LastOrDefault();
                if (!string.IsNullOrEmpty(lastDir))
                    tab.DisambiguatedTitle = $"{lastDir}/{tab.Title}";
            }
        }
    }
}