using System.Text.Json;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class SessionService : ISessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _sessionsDir;

    public SessionService()
    {
        _sessionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cominomi", "sessions");
        Directory.CreateDirectory(_sessionsDir);
    }

    public async Task<List<Session>> GetSessionsAsync()
    {
        var sessions = new List<Session>();
        if (!Directory.Exists(_sessionsDir))
            return sessions;

        foreach (var file in Directory.GetFiles(_sessionsDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var session = JsonSerializer.Deserialize<Session>(json, JsonOptions);
                if (session != null)
                {
                    // Don't load full messages for the list view
                    sessions.Add(new Session
                    {
                        Id = session.Id,
                        Title = session.Title,
                        WorkingDirectory = session.WorkingDirectory,
                        Model = session.Model,
                        WorkspaceId = session.WorkspaceId,
                        PermissionMode = session.PermissionMode,
                        CreatedAt = session.CreatedAt,
                        UpdatedAt = session.UpdatedAt
                    });
                }
            }
            catch
            {
                // skip corrupted files
            }
        }

        return sessions.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    public async Task<List<Session>> GetSessionsByWorkspaceAsync(string workspaceId)
    {
        var all = await GetSessionsAsync();
        return all.Where(s => s.WorkspaceId == workspaceId).ToList();
    }

    public Task<Session> CreateSessionAsync(string workingDir, string model, string workspaceId = "default")
    {
        var session = new Session
        {
            WorkingDirectory = workingDir,
            Model = model,
            WorkspaceId = workspaceId
        };
        return Task.FromResult(session);
    }

    public async Task<Session?> LoadSessionAsync(string sessionId)
    {
        var path = Path.Combine(_sessionsDir, $"{sessionId}.json");
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Session>(json, JsonOptions);
    }

    public async Task SaveSessionAsync(Session session)
    {
        session.UpdatedAt = DateTime.UtcNow;

        // Auto-generate title from first user message
        if (session.Title == "New Chat" && session.Messages.Count > 0)
        {
            var firstMessage = session.Messages.FirstOrDefault(m => m.Role == MessageRole.User);
            if (firstMessage != null)
            {
                session.Title = firstMessage.Text.Length > 50
                    ? firstMessage.Text[..50] + "..."
                    : firstMessage.Text;
            }
        }

        var path = Path.Combine(_sessionsDir, $"{session.Id}.json");
        var json = JsonSerializer.Serialize(session, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public Task DeleteSessionAsync(string sessionId)
    {
        var path = Path.Combine(_sessionsDir, $"{sessionId}.json");
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }
}
