namespace Seoro.Shared.Services.Sessions.History;

/// <summary>CLI 네이티브 세션 파일 한 개와 그 프로젝트 메타데이터.</summary>
public sealed record ClaudeSessionFile(string FilePath, string ProjectHash, string ProjectPath);

/// <summary>
///     `~/.claude/projects/` 디렉토리 접근을 단일 지점으로 캡슐화한다.
///     세션 파일 열거와 프로젝트 해시→경로 복원만 담당하며, 파일 내용은 해석하지 않는다.
///     History 도메인의 다른 서비스(Index/Search/Live)가 직접 파일시스템을 건드리지 않도록 한다.
/// </summary>
public interface IClaudeProjectStore
{
    bool ProjectsDirectoryExists { get; }

    /// <summary>모든 프로젝트 디렉토리의 *.jsonl 세션 파일을 지연 열거한다 (접근 불가 항목은 건너뜀).</summary>
    IEnumerable<ClaudeSessionFile> EnumerateSessionFiles();

    /// <summary>Claude의 프로젝트 디렉토리 해시(`-` 인코딩)를 실제 경로로 복원한다.</summary>
    string HashToPath(string hash);
}

public class ClaudeProjectStore : IClaudeProjectStore
{
    private static readonly string ProjectsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    public bool ProjectsDirectoryExists => Directory.Exists(ProjectsDir);

    public IEnumerable<ClaudeSessionFile> EnumerateSessionFiles()
    {
        if (!Directory.Exists(ProjectsDir))
            yield break;

        foreach (var projectDir in Directory.GetDirectories(ProjectsDir))
        {
            var projectHash = Path.GetFileName(projectDir);
            var projectPath = HashToPath(projectHash);

            string[] files;
            try
            {
                files = Directory.GetFiles(projectDir, "*.jsonl");
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
                yield return new ClaudeSessionFile(file, projectHash, projectPath);
        }
    }

    public string HashToPath(string hash)
    {
        // Naive: replace all '-' with '/'
        var naive = hash.Replace('-', '/');
        if (Directory.Exists(naive))
            return naive;

        // Smart: try to find a real path by grouping segments
        var segments = hash.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var resolved = ResolveSegments(segments, 0, "/");
        return resolved ?? naive;
    }

    private static string? ResolveSegments(string[] segments, int idx, string current)
    {
        if (idx >= segments.Length)
            return Directory.Exists(current) ? current : null;

        // Try joining segments with '-' (longer matches first to prefer real dir names)
        for (var end = segments.Length; end > idx; end--)
        {
            var joined = string.Join("-", segments[idx..end]);
            var candidate = current == "/"
                ? $"/{joined}"
                : $"{current}/{joined}";

            if (Directory.Exists(candidate))
            {
                var result = ResolveSegments(segments, end, candidate);
                if (result != null) return result;
            }
        }

        return null;
    }
}
