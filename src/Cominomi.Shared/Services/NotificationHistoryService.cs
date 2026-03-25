using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class NotificationHistoryService : INotificationHistoryService
{
    private const int MaxEntries = 100;
    private readonly List<NotificationRecord> _entries = new();
    private readonly object _lock = new();

    public IReadOnlyList<NotificationRecord> Entries
    {
        get
        {
            lock (_lock)
                return _entries.ToList();
        }
    }

    public int UnreadCount
    {
        get
        {
            lock (_lock)
                return _entries.Count(e => !e.IsRead);
        }
    }

    public void Record(string title, string body, NotificationType type, string? sessionId = null)
    {
        lock (_lock)
        {
            _entries.Insert(0, new NotificationRecord
            {
                Title = title,
                Body = body,
                Type = type,
                SessionId = sessionId
            });

            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
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

    public void MarkAllAsRead()
    {
        lock (_lock)
        {
            foreach (var entry in _entries)
                entry.IsRead = true;
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
            {
                if (entry.SessionId == sessionId && !entry.IsRead)
                {
                    entry.IsRead = true;
                    changed = true;
                }
            }
        }

        if (changed)
            OnChange?.Invoke();
    }

    public event Action? OnChange;
}
