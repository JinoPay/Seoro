using System.Text.Json;

namespace Seoro.Shared.Services.Sessions.History;

/// <summary>
///     세션별 사용자 태그/메모를 `seoro-session-tags.json`에 읽고 쓰는 저장소.
/// </summary>
public interface ISessionTagStore
{
    Task<SessionTagsData> GetTagsAsync();
    Task SetTagAsync(string sessionId, List<string> tags, string? note = null);
}

public class SessionTagStore : ISessionTagStore
{
    private static readonly string TagsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "seoro-session-tags.json");

    public Task<SessionTagsData> GetTagsAsync()
    {
        return Task.Run(() =>
        {
            if (!File.Exists(TagsFilePath))
                return new SessionTagsData();

            try
            {
                var json = File.ReadAllText(TagsFilePath);
                return JsonSerializer.Deserialize<SessionTagsData>(json) ?? new SessionTagsData();
            }
            catch
            {
                return new SessionTagsData();
            }
        });
    }

    public async Task SetTagAsync(string sessionId, List<string> tags, string? note = null)
    {
        var data = await GetTagsAsync();

        if (tags.Count > 0)
            data.Tags[sessionId] = tags;
        else
            data.Tags.Remove(sessionId);

        if (note != null)
        {
            if (!string.IsNullOrEmpty(note))
                data.Notes[sessionId] = note;
            else
                data.Notes.Remove(sessionId);
        }

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(TagsFilePath, json);
    }
}
