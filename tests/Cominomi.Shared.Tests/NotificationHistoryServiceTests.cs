using Cominomi.Shared.Services;

namespace Cominomi.Shared.Tests;

public class NotificationHistoryServiceTests
{
    [Fact]
    public void Record_AddsEntryToHistory()
    {
        var svc = new NotificationHistoryService();

        svc.Record("Tokyo", "Task completed", NotificationType.Info, "session-1");

        Assert.Single(svc.Entries);
        Assert.Equal("Tokyo", svc.Entries[0].Title);
        Assert.Equal("Task completed", svc.Entries[0].Body);
        Assert.Equal(NotificationType.Info, svc.Entries[0].Type);
        Assert.Equal("session-1", svc.Entries[0].SessionId);
        Assert.False(svc.Entries[0].IsRead);
    }

    [Fact]
    public void Record_NewestFirst()
    {
        var svc = new NotificationHistoryService();

        svc.Record("Tokyo", "First", NotificationType.Info);
        svc.Record("Seoul", "Second", NotificationType.Success);

        Assert.Equal("Seoul", svc.Entries[0].Title);
        Assert.Equal("Tokyo", svc.Entries[1].Title);
    }

    [Fact]
    public void UnreadCount_TracksUnreadEntries()
    {
        var svc = new NotificationHistoryService();

        svc.Record("Tokyo", "A", NotificationType.Info);
        svc.Record("Seoul", "B", NotificationType.Success);

        Assert.Equal(2, svc.UnreadCount);
    }

    [Fact]
    public void MarkAsRead_MarksSpecificEntry()
    {
        var svc = new NotificationHistoryService();

        svc.Record("Tokyo", "A", NotificationType.Info);
        svc.Record("Seoul", "B", NotificationType.Success);

        var id = svc.Entries[0].Id;
        svc.MarkAsRead(id);

        Assert.Equal(1, svc.UnreadCount);
        Assert.True(svc.Entries[0].IsRead);
        Assert.False(svc.Entries[1].IsRead);
    }

    [Fact]
    public void MarkAllAsRead_MarksAllEntries()
    {
        var svc = new NotificationHistoryService();

        svc.Record("A", "a", NotificationType.Info);
        svc.Record("B", "b", NotificationType.Success);
        svc.Record("C", "c", NotificationType.Error);

        svc.MarkAllAsRead();

        Assert.Equal(0, svc.UnreadCount);
        Assert.All(svc.Entries, e => Assert.True(e.IsRead));
    }

    [Fact]
    public void Record_CapsAtMaxEntries()
    {
        var svc = new NotificationHistoryService();

        for (int i = 0; i < 110; i++)
            svc.Record($"Title{i}", $"Body{i}", NotificationType.Info);

        Assert.Equal(100, svc.Entries.Count);
        Assert.Equal("Title109", svc.Entries[0].Title);
    }

    [Fact]
    public void OnChange_FiresOnRecord()
    {
        var svc = new NotificationHistoryService();
        var fired = false;
        svc.OnChange += () => fired = true;

        svc.Record("Test", "body", NotificationType.Info);

        Assert.True(fired);
    }

    [Fact]
    public void OnChange_FiresOnMarkAsRead()
    {
        var svc = new NotificationHistoryService();
        svc.Record("Test", "body", NotificationType.Info);

        var fired = false;
        svc.OnChange += () => fired = true;

        svc.MarkAsRead(svc.Entries[0].Id);

        Assert.True(fired);
    }

    [Fact]
    public void MarkSessionAsRead_MarksOnlyMatchingSession()
    {
        var svc = new NotificationHistoryService();

        svc.Record("Tokyo", "A", NotificationType.Info, "session-1");
        svc.Record("Seoul", "B", NotificationType.Success, "session-2");
        svc.Record("Tokyo", "C", NotificationType.Question, "session-1");

        svc.MarkSessionAsRead("session-1");

        Assert.Equal(1, svc.UnreadCount);
        Assert.True(svc.Entries[0].IsRead);   // C (session-1)
        Assert.False(svc.Entries[1].IsRead);   // B (session-2)
        Assert.True(svc.Entries[2].IsRead);    // A (session-1)
    }

    [Fact]
    public void MarkSessionAsRead_FiresOnChange()
    {
        var svc = new NotificationHistoryService();
        svc.Record("Tokyo", "A", NotificationType.Info, "session-1");

        var fired = false;
        svc.OnChange += () => fired = true;

        svc.MarkSessionAsRead("session-1");

        Assert.True(fired);
    }

    [Fact]
    public void MarkSessionAsRead_NoChangeSkipsEvent()
    {
        var svc = new NotificationHistoryService();
        svc.Record("Tokyo", "A", NotificationType.Info, "session-1");

        var fired = false;
        svc.OnChange += () => fired = true;

        svc.MarkSessionAsRead("session-999");

        Assert.False(fired);
    }

    [Fact]
    public void OnChange_FiresOnMarkAllAsRead()
    {
        var svc = new NotificationHistoryService();
        svc.Record("Test", "body", NotificationType.Info);

        var fired = false;
        svc.OnChange += () => fired = true;

        svc.MarkAllAsRead();

        Assert.True(fired);
    }
}
