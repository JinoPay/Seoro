using Seoro.Shared.Models;
using Seoro.Shared.Models.ViewModels;
using Seoro.Shared.Services;

namespace Seoro.Shared.Tests;

public class TabManagerTests
{
    [Fact]
    public void OpenFileContentTab_TruncatesLargeFiles()
    {
        var mgr = new TabManager();
        mgr.EnsureChatTab();

        var largeContent = new string('x', (int)(TabManager.MaxSingleFileSizeBytes / 2) + 1000);
        mgr.OpenFileContentTab("big.txt", largeContent);

        var tab = mgr.OpenTabs.First(t => t.Type == MainTabType.FileContent);
        Assert.Contains("truncated", tab.FileContent!);
    }

    [Fact]
    public void EvictLruContent_EvictsOldestInactiveTabs()
    {
        var mgr = new TabManager();
        mgr.EnsureChatTab();

        var charsFor9MB = (int)(9 * 1024 * 1024 / 2);
        var content = new string('a', charsFor9MB);

        for (int i = 1; i <= 5; i++)
        {
            mgr.OpenFileContentTab($"file{i}.txt", content);
            var t = mgr.OpenTabs.First(t => t.FilePath == $"file{i}.txt");
            t.LastAccessedAt = DateTime.UtcNow.AddMinutes(-10 + i);
        }

        Assert.All(mgr.OpenTabs.Where(t => t.Type == MainTabType.FileContent),
            t => Assert.False(t.ContentEvicted));

        mgr.OpenFileContentTab("file6.txt", content);

        var file1 = mgr.OpenTabs.First(t => t.FilePath == "file1.txt");
        Assert.True(file1.ContentEvicted);
        Assert.Null(file1.FileContent);

        var file6 = mgr.OpenTabs.First(t => t.FilePath == "file6.txt");
        Assert.False(file6.ContentEvicted);
        Assert.NotNull(file6.FileContent);
    }

    [Fact]
    public void EvictLruContent_NeverEvictsActiveTab()
    {
        var mgr = new TabManager();
        mgr.EnsureChatTab();

        var charsFor9MB = (int)(9 * 1024 * 1024 / 2);
        var content = new string('b', charsFor9MB);

        for (int i = 1; i <= 5; i++)
        {
            mgr.OpenFileContentTab($"old{i}.txt", content);
            mgr.OpenTabs.First(t => t.FilePath == $"old{i}.txt").LastAccessedAt = DateTime.UtcNow.AddMinutes(-10);
        }

        mgr.OpenFileContentTab("new.txt", content);
        var newTab = mgr.OpenTabs.First(t => t.FilePath == "new.txt");
        Assert.False(newTab.ContentEvicted);
        Assert.NotNull(newTab.FileContent);

        var evictedCount = mgr.OpenTabs.Count(t => t.ContentEvicted);
        Assert.True(evictedCount >= 1);
    }

    [Fact]
    public void OpenFileContentTab_ReloadsEvictedTab()
    {
        var mgr = new TabManager();
        mgr.EnsureChatTab();

        mgr.OpenFileContentTab("test.txt", "hello");
        var tab = mgr.OpenTabs.First(t => t.FilePath == "test.txt");

        tab.FileContent = null;
        tab.ContentEvicted = true;

        mgr.OpenFileContentTab("test.txt", "hello again");

        Assert.False(tab.ContentEvicted);
        Assert.Equal("hello again", tab.FileContent);
    }

    [Fact]
    public void SetActiveTab_UpdatesLastAccessedAt()
    {
        var mgr = new TabManager();
        mgr.EnsureChatTab();

        mgr.OpenFileContentTab("test.txt", "content");
        var tab = mgr.OpenTabs.First(t => t.FilePath == "test.txt");
        var before = tab.LastAccessedAt;

        Thread.Sleep(10);
        mgr.SetActiveTab(tab.Id);

        Assert.True(tab.LastAccessedAt > before);
    }

    [Fact]
    public void Reset_ClearsAllContentAndTabs()
    {
        var mgr = new TabManager();
        mgr.EnsureChatTab();

        mgr.OpenFileContentTab("a.txt", "content a");
        mgr.OpenFileContentTab("b.txt", "content b");
        Assert.Equal(3, mgr.OpenTabs.Count);

        mgr.Reset();

        Assert.Single(mgr.OpenTabs);
        Assert.Equal(MainTabType.Chat, mgr.OpenTabs[0].Type);
    }

    [Fact]
    public void OpenFileContentTab_InitializesOriginalContentAndCleanState()
    {
        var mgr = new TabManager();
        mgr.EnsureChatTab();

        mgr.OpenFileContentTab("a.txt", "hello", DateTime.UtcNow);

        var tab = mgr.OpenTabs.First(t => t.FilePath == "a.txt");
        Assert.Equal("hello", tab.OriginalContent);
        Assert.False(tab.IsDirty);
        Assert.NotNull(tab.LastLoadedMtimeUtc);
    }

    [Fact]
    public void MarkDirty_SetsDirtyFlag()
    {
        var mgr = new TabManager();
        mgr.EnsureChatTab();
        mgr.OpenFileContentTab("a.txt", "hello");

        mgr.MarkDirty("a.txt");

        var tab = mgr.OpenTabs.First(t => t.FilePath == "a.txt");
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public void MarkSaved_UpdatesOriginalContentAndClearsDirty()
    {
        var mgr = new TabManager();
        mgr.EnsureChatTab();
        mgr.OpenFileContentTab("a.txt", "hello");
        mgr.MarkDirty("a.txt");
        var mtime = DateTime.UtcNow;

        mgr.MarkSaved("a.txt", "hello world", mtime);

        var tab = mgr.OpenTabs.First(t => t.FilePath == "a.txt");
        Assert.False(tab.IsDirty);
        Assert.Equal("hello world", tab.OriginalContent);
        Assert.Equal("hello world", tab.FileContent);
        Assert.Equal(mtime, tab.LastLoadedMtimeUtc);
        Assert.NotNull(tab.LastSavedAt);
    }

    [Fact]
    public void EvictLruContent_SkipsDirtyTabs()
    {
        var mgr = new TabManager();
        mgr.EnsureChatTab();

        var charsFor9MB = (int)(9 * 1024 * 1024 / 2);
        var content = new string('c', charsFor9MB);

        for (var i = 1; i <= 5; i++)
        {
            mgr.OpenFileContentTab($"file{i}.txt", content);
            mgr.OpenTabs.First(t => t.FilePath == $"file{i}.txt").LastAccessedAt = DateTime.UtcNow.AddMinutes(-10 + i);
        }

        // 가장 오래된 file1을 더티로 표시 — eviction 후보에서 제외되어야 함
        mgr.MarkDirty("file1.txt");

        mgr.OpenFileContentTab("file6.txt", content);

        var file1 = mgr.OpenTabs.First(t => t.FilePath == "file1.txt");
        Assert.False(file1.ContentEvicted);
        Assert.NotNull(file1.FileContent);

        // 더티가 아닌 가장 오래된 탭(file2)이 대신 evict
        var file2 = mgr.OpenTabs.First(t => t.FilePath == "file2.txt");
        Assert.True(file2.ContentEvicted);
    }

    [Fact]
    public void OpenFileContentTab_ReopenResetsDirty()
    {
        var mgr = new TabManager();
        mgr.EnsureChatTab();
        mgr.OpenFileContentTab("a.txt", "hello");
        mgr.MarkDirty("a.txt");
        Assert.True(mgr.OpenTabs.First(t => t.FilePath == "a.txt").IsDirty);

        mgr.OpenFileContentTab("a.txt", "hello fresh");

        var tab = mgr.OpenTabs.First(t => t.FilePath == "a.txt");
        Assert.False(tab.IsDirty);
        Assert.Equal("hello fresh", tab.OriginalContent);
    }

}
