using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services.Sessions.History;

/// <summary>
///     CLI 네이티브 세션 파일을 가볍게 스캔(첫 N줄 + 파일 크기 추정)하여
///     메타데이터 인덱스(`seoro-session-index.json`)를 구성·영속하고,
///     인덱스에서 세션 목록을 페이지네이션으로 제공한다.
/// </summary>
public interface ISessionIndexService
{
    Task RefreshSessionIndexAsync(bool force = false);
    Task<SessionListResult> ListSessionsAsync(int limit = 10, int offset = 0);
}

public class SessionIndexService(IClaudeProjectStore projectStore, ILogger<SessionIndexService> logger)
    : ISessionIndexService
{
    private const int SampleLineCount = 30;

    private static readonly JsonSerializerOptions IndexJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly string IndexFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "seoro-session-index.json");

    private readonly SemaphoreSlim _indexLock = new(1, 1);

    private volatile SessionIndex? _index;

    public async Task RefreshSessionIndexAsync(bool force = false)
    {
        await _indexLock.WaitAsync();
        try
        {
            await EnsureIndexLoadedAsync();
            var index = _index!;

            if (!projectStore.ProjectsDirectoryExists)
                return;

            var existingPaths = new HashSet<string>(index.Entries.Keys, StringComparer.OrdinalIgnoreCase);
            var currentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pf in projectStore.EnumerateSessionFiles())
                try
                {
                    var fi = new FileInfo(pf.FilePath);
                    if (fi.Length < 200) continue;
                    currentPaths.Add(pf.FilePath);

                    // Skip unchanged files unless force
                    if (!force && index.Entries.TryGetValue(pf.FilePath, out var existing)
                               && existing.FileSizeBytes == fi.Length
                               && existing.FileLastWriteUtc == fi.LastWriteTimeUtc)
                        continue;

                    var entry = QuickScan(pf, fi);
                    if (entry != null)
                        index.Entries[pf.FilePath] = entry;
                }
                catch
                {
                    /* skip inaccessible files */
                }

            // Remove entries for deleted files
            foreach (var path in existingPaths)
                if (!currentPaths.Contains(path))
                    index.Entries.Remove(path);

            index.LastComputedDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            await SaveIndexAsync(index);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    public async Task<SessionListResult> ListSessionsAsync(int limit = 10, int offset = 0)
    {
        await EnsureIndexLoadedAsync();
        var index = _index!;

        var sorted = index.Entries.Values
            .OrderByDescending(e => e.FirstTimestamp ?? DateTime.MinValue)
            .ToList();

        var total = sorted.Count;
        var sessions = sorted
            .Skip(offset)
            .Take(limit)
            .Select(IndexEntryToSummary)
            .ToList();

        return new SessionListResult
        {
            Sessions = sessions,
            Total = total,
            HasMore = offset + limit < total
        };
    }

    private SessionIndexEntry? QuickScan(ClaudeSessionFile pf, FileInfo fi)
    {
        try
        {
            using var fs = new FileStream(pf.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);

            DateTime? firstTimestamp = null;
            DateTime? lastTimestamp = null;
            string? firstMessage = null;
            int parsedCount = 0, userCount = 0, toolCount = 0;
            long totalBytesRead = 0;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                totalBytesRead += Encoding.UTF8.GetByteCount(line) + 1; // +1 for newline

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

                parsedCount++;

                var ts = ReplayJsonHelpers.ParseTimestampNode(node["timestamp"]);
                if (ts != null)
                {
                    firstTimestamp ??= ts;
                    lastTimestamp = ts;
                }

                if (type == "user")
                {
                    if (ReplayJsonHelpers.IsToolResultOnlyUser(node)) continue;
                    userCount++;
                    if (firstMessage == null)
                    {
                        var msg = node["message"];
                        var content = msg?["content"];
                        var text = ReplayJsonHelpers.ExtractTextContent(content);
                        if (!string.IsNullOrEmpty(text))
                            firstMessage = text.Length > 100 ? text[..100] : text;
                    }
                }
                else if (type == "assistant")
                {
                    var msg = node["message"];
                    var content = msg?["content"];
                    if (content is JsonArray arr)
                        foreach (var item in arr)
                            if (item?["type"]?.GetValue<string>() == "tool_use")
                                toolCount++;
                }

                // After SampleLineCount parsed entries, estimate the rest from file size
                if (parsedCount >= SampleLineCount)
                    break;
            }

            if (parsedCount < 3) return null;

            // Estimate total entries from file size and average line bytes
            int estimatedEntries;
            int estimatedUsers;
            int estimatedTools;

            if (reader.EndOfStream || parsedCount < SampleLineCount)
            {
                // File fully read within sample — exact counts
                estimatedEntries = parsedCount;
                estimatedUsers = userCount;
                estimatedTools = toolCount;
            }
            else
            {
                var avgLineBytes = totalBytesRead / Math.Max(parsedCount, 1);
                estimatedEntries = (int)(fi.Length / Math.Max(avgLineBytes, 1));
                var scale = (float)estimatedEntries / parsedCount;
                estimatedUsers = (int)(userCount * scale);
                estimatedTools = (int)(toolCount * scale);
            }

            return new SessionIndexEntry
            {
                Id = Path.GetFileNameWithoutExtension(pf.FilePath),
                FilePath = pf.FilePath,
                ProjectHash = pf.ProjectHash,
                ProjectPath = pf.ProjectPath,
                EntryCount = estimatedEntries,
                UserMessageCount = estimatedUsers,
                ToolCallCount = estimatedTools,
                FirstTimestamp = firstTimestamp,
                LastTimestamp = lastTimestamp,
                FirstMessage = firstMessage,
                FileSizeBytes = fi.Length,
                FileLastWriteUtc = fi.LastWriteTimeUtc,
                IsEstimated = parsedCount >= SampleLineCount && !reader.EndOfStream
            };
        }
        catch
        {
            return null;
        }
    }

    private static SessionReplaySummary IndexEntryToSummary(SessionIndexEntry e)
    {
        return new SessionReplaySummary
        {
            Id = e.Id,
            FilePath = e.FilePath,
            ProjectHash = e.ProjectHash,
            ProjectPath = e.ProjectPath,
            EntryCount = e.EntryCount,
            MessageCount = e.UserMessageCount,
            ToolCallCount = e.ToolCallCount,
            FirstTimestamp = e.FirstTimestamp,
            LastTimestamp = e.LastTimestamp,
            FirstMessage = e.FirstMessage,
            ModifiedAtUnix = e.FileLastWriteUtc.Subtract(DateTime.UnixEpoch).TotalSeconds
        };
    }

    private async Task EnsureIndexLoadedAsync()
    {
        if (_index != null) return;

        try
        {
            if (File.Exists(IndexFilePath))
            {
                var json = await File.ReadAllTextAsync(IndexFilePath);
                _index = JsonSerializer.Deserialize<SessionIndex>(json, IndexJsonOptions);
            }
        }
        catch
        {
            // Corrupted index — start fresh
        }

        _index ??= new SessionIndex();
    }

    private async Task SaveIndexAsync(SessionIndex index)
    {
        try
        {
            var json = JsonSerializer.Serialize(index, IndexJsonOptions);
            await File.WriteAllTextAsync(IndexFilePath, json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "세션 인덱스 파일 쓰기 실패");
        }
    }
}
