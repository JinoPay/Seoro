using System.Text.Json.Nodes;

namespace Seoro.Shared.Services.Sessions.History;

/// <summary>
///     CLI 네이티브 세션 파일을 라인 단위로 훑어 쿼리에 매칭되는 텍스트 스니펫을 찾는
///     풀텍스트 검색(인덱스 기반이 아닌 실시간 파일 스캔).
/// </summary>
public interface ISessionSearchService
{
    Task<List<SessionSearchResult>> SearchAsync(string query, int maxResults = 20);
}

public class SessionSearchService(IClaudeProjectStore projectStore) : ISessionSearchService
{
    public Task<List<SessionSearchResult>> SearchAsync(string query, int maxResults = 20)
    {
        return Task.Run(() =>
        {
            var results = new List<SessionSearchResult>();
            if (!projectStore.ProjectsDirectoryExists || string.IsNullOrWhiteSpace(query))
                return results;

            var q = query.ToLowerInvariant();

            foreach (var pf in projectStore.EnumerateSessionFiles())
            {
                if (results.Count >= maxResults) break;

                var sessionId = Path.GetFileNameWithoutExtension(pf.FilePath);

                try
                {
                    using var fs = new FileStream(pf.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);

                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (!line.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;

                        JsonNode? node;
                        try
                        {
                            node = JsonNode.Parse(line);
                        }
                        catch
                        {
                            continue;
                        }

                        if (node == null) continue;

                        var type = node["type"]?.GetValue<string>() ?? "";
                        if (ReplayJsonHelpers.NoiseEventTypes.Contains(type)) continue;

                        var snippet = ReplayJsonHelpers.ExtractTextSnippet(node, q);
                        if (string.IsNullOrEmpty(snippet)) continue;

                        var ts = ReplayJsonHelpers.ParseTimestampNode(node["timestamp"]);

                        results.Add(new SessionSearchResult
                        {
                            SessionId = sessionId,
                            ProjectPath = pf.ProjectPath,
                            FilePath = pf.FilePath,
                            Snippet = snippet,
                            Timestamp = ts,
                            EventType = type
                        });

                        if (results.Count >= maxResults) break;
                    }
                }
                catch
                {
                    /* skip unreadable files */
                }
            }

            return results;
        });
    }
}
