namespace Seoro.Shared.Services.Sessions.History;

/// <summary>최근(5분 이내) 수정된 CLI 네이티브 세션 파일을 "라이브" 세션으로 감지한다.</summary>
public interface ILiveSessionDetector
{
    Task<List<LiveSessionInfo>> DetectLiveSessionsAsync();
}

public class LiveSessionDetector(IClaudeProjectStore projectStore) : ILiveSessionDetector
{
    public Task<List<LiveSessionInfo>> DetectLiveSessionsAsync()
    {
        return Task.Run(() =>
        {
            var live = new List<LiveSessionInfo>();
            if (!projectStore.ProjectsDirectoryExists)
                return live;

            var now = DateTimeOffset.UtcNow;

            foreach (var pf in projectStore.EnumerateSessionFiles())
                try
                {
                    var fi = new FileInfo(pf.FilePath);
                    var ago = (long)(now - fi.LastWriteTimeUtc).TotalSeconds;
                    if (ago < 300) // 5 minutes
                        live.Add(new LiveSessionInfo
                        {
                            FilePath = pf.FilePath,
                            ProjectPath = pf.ProjectPath,
                            ModifiedSecondsAgo = ago
                        });
                }
                catch
                {
                    /* skip */
                }

            live.Sort((a, b) => a.ModifiedSecondsAgo.CompareTo(b.ModifiedSecondsAgo));
            return live;
        });
    }
}
