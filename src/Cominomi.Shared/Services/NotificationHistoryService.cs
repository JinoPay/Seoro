using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class NotificationHistoryService : INotificationHistoryService
{
    private const int MaxEntries = 100;
    private readonly List<NotificationRecord> _entries = [];
    private readonly Lock _lock = new();

    public event Action? OnChange;

    public int UnreadCount
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count(e => !e.IsRead);
            }
        }
    }

    public IReadOnlyList<NotificationRecord> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }
    }

    public void MarkAllAsRead()
    {
        lock (_lock)
        {
            foreach (var entry in _entries)
                entry.IsRead = true;
        }

        OnChange?.Invoke();
    }

    public void MarkAsRead(string id)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id);
            if (entry != null) entry.IsRead = true;
        }

        OnChange?.Invoke();
    }

    public void MarkSessionAsRead(string sessionId)
    {
        bool changed;
        lock (_lock)
        {
            changed = false;
            foreach (var entry in _entries)
                if (entry.SessionId == sessionId && !entry.IsRead)
                {
                    entry.IsRead = true;
                    changed = true;
                }
        }

        if (changed)
            OnChange?.Invoke();
    }

    public void Record(string title, string body, NotificationType type, string? sessionId = null, bool isRead = false)
    {
        lock (_lock)
        {
            _entries.Insert(0, new NotificationRecord
            {
                Title = title,
                Body = body,
                Type = type,
                SessionId = sessionId,
                IsRead = isRead
            });

            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }

        OnChange?.Invoke();
    }
}