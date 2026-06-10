using System.Text.Json;
using System.Text.RegularExpressions;

namespace Seoro.Shared.Services.Chat;

/// <summary>
///     Claude CLI의 TaskCreate/TaskUpdate 도구 호출을 세션 단위로 누적하여
///     기존 TodoSnapshot UI(플로터/인라인 스트립)로 변환하는 트래커.
///     TodoWrite(구버전 CLI)와 달리 Task 도구는 증분 업데이트이므로 상태를 유지해야 한다.
/// </summary>
public sealed partial class TaskListTracker
{
    private readonly object _lock = new();
    private readonly List<TrackedTask> _tasks = [];
    private int _nextId = 1;

    public bool HasEntries
    {
        get
        {
            lock (_lock)
            {
                return _tasks.Count > 0;
            }
        }
    }

    /// <summary>세션 메시지 히스토리에서 재구성을 시도했는지 여부 (세션 전환마다 재스캔 방지).</summary>
    public bool RebuildAttempted { get; private set; }

    public static bool IsTaskTool(string? name) => IsTaskCreate(name) || IsTaskUpdate(name);

    public static bool IsTaskCreate(string? name) =>
        string.Equals(name, "TaskCreate", StringComparison.OrdinalIgnoreCase);

    public static bool IsTaskUpdate(string? name) =>
        string.Equals(name, "TaskUpdate", StringComparison.OrdinalIgnoreCase);

    /// <summary>TaskCreate 입력({subject, description, activeForm?})을 적용. 같은 toolUseId는 갱신만 한다.</summary>
    public bool ApplyTaskCreate(string toolUseId, string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var subject = root.TryGetProperty("subject", out var s) ? s.GetString() : null;
            if (string.IsNullOrWhiteSpace(subject)) return false;
            var activeForm = root.TryGetProperty("activeForm", out var a) ? a.GetString() ?? "" : "";

            lock (_lock)
            {
                var existing = _tasks.FirstOrDefault(t => t.ToolUseId == toolUseId);
                if (existing != null)
                {
                    existing.Subject = subject;
                    existing.ActiveForm = activeForm;
                    return true;
                }

                _tasks.Add(new TrackedTask
                {
                    // CLI는 세션 내에서 1부터 순차 번호를 부여하므로 생성 순번을 잠정 ID로 사용.
                    // 실제 ID는 tool_result("Task #N created successfully")에서 보정된다.
                    TaskId = (_nextId++).ToString(),
                    ToolUseId = toolUseId,
                    Subject = subject,
                    ActiveForm = activeForm
                });
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>TaskCreate의 tool_result 텍스트에서 CLI가 부여한 실제 taskId를 추출해 잠정 ID를 보정.</summary>
    public void ApplyTaskCreateResult(string toolUseId, string? output)
    {
        if (string.IsNullOrEmpty(output)) return;
        var m = TaskIdPattern().Match(output);
        if (!m.Success) return;

        lock (_lock)
        {
            var task = _tasks.FirstOrDefault(t => t.ToolUseId == toolUseId);
            if (task == null) return;
            task.TaskId = m.Groups[1].Value;
            // 재개된 CLI 세션은 이전 턴의 번호를 이어가므로 다음 잠정 ID도 맞춰준다
            if (int.TryParse(task.TaskId, out var n) && n >= _nextId)
                _nextId = n + 1;
        }
    }

    /// <summary>TaskUpdate 입력({taskId, status?, subject?, activeForm?})을 적용. status "deleted"는 항목 제거.</summary>
    public bool ApplyTaskUpdate(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var taskId = root.TryGetProperty("taskId", out var idEl)
                ? idEl.ValueKind == JsonValueKind.Number ? idEl.GetRawText() : idEl.GetString()
                : null;
            if (string.IsNullOrEmpty(taskId)) return false;

            lock (_lock)
            {
                var task = _tasks.FirstOrDefault(t => t.TaskId == taskId);
                if (task == null) return false;

                if (root.TryGetProperty("status", out var st) && st.GetString() is { } status)
                {
                    if (status.Equals("deleted", StringComparison.OrdinalIgnoreCase))
                    {
                        _tasks.Remove(task);
                        return true;
                    }

                    task.Status = ParseStatus(status);
                }

                if (root.TryGetProperty("subject", out var subj) && subj.GetString() is { Length: > 0 } newSubject)
                    task.Subject = newSubject;
                if (root.TryGetProperty("activeForm", out var af) && af.GetString() is { } newActiveForm)
                    task.ActiveForm = newActiveForm;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>TodoWrite(전체 목록 교체 방식)가 도착하면 그쪽을 신뢰하고 트래커를 재구성.</summary>
    public void ResetFromTodoWrite(TodoSnapshot snapshot)
    {
        lock (_lock)
        {
            _tasks.Clear();
            _nextId = 1;
            foreach (var entry in snapshot.Entries)
                _tasks.Add(new TrackedTask
                {
                    TaskId = (_nextId++).ToString(),
                    ToolUseId = $"todowrite-{_nextId}",
                    Subject = entry.Content,
                    ActiveForm = entry.ActiveForm,
                    Status = entry.Status
                });
        }
    }

    /// <summary>
    ///     저장된 메시지 히스토리에서 TodoWrite/TaskCreate/TaskUpdate 호출을 순서대로 재적용해 트래커를 재구성.
    ///     앱 재시작 후 세션을 다시 열 때 플로터 상태를 복원하는 데 사용한다.
    /// </summary>
    public void RebuildFromMessages(IEnumerable<ChatMessage> messages)
    {
        lock (_lock)
        {
            RebuildAttempted = true;
            _tasks.Clear();
            _nextId = 1;
        }

        foreach (var message in messages)
        foreach (var tool in message.ToolCalls)
        {
            if (TodoSnapshotParser.IsTodoWriteTool(tool.Name))
            {
                if (TodoSnapshotParser.TryParse(tool.Input, out var snap))
                    ResetFromTodoWrite(snap);
            }
            else if (IsTaskCreate(tool.Name))
            {
                if (ApplyTaskCreate(tool.Id, tool.Input) && !tool.IsError)
                    ApplyTaskCreateResult(tool.Id, tool.Output);
            }
            else if (IsTaskUpdate(tool.Name))
            {
                ApplyTaskUpdate(tool.Input);
            }
        }
    }

    public TodoSnapshot ToSnapshot()
    {
        lock (_lock)
        {
            return new TodoSnapshot
            {
                Entries = _tasks
                    .Select(t => new TodoEntry(t.Subject, t.ActiveForm, t.Status))
                    .ToList(),
                UpdatedAt = DateTime.UtcNow
            };
        }
    }

    private static TodoStatus ParseStatus(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "completed" or "done" => TodoStatus.Completed,
            "in_progress" or "inprogress" or "in-progress" or "running" => TodoStatus.InProgress,
            _ => TodoStatus.Pending
        };
    }

    // tool_result 텍스트 예: "Task #3 created successfully: Verify rename"
    [GeneratedRegex(@"[Tt]ask\s+#(\d+)")]
    private static partial Regex TaskIdPattern();

    private sealed class TrackedTask
    {
        public required string TaskId { get; set; }
        public required string ToolUseId { get; init; }
        public string Subject { get; set; } = "";
        public string ActiveForm { get; set; } = "";
        public TodoStatus Status { get; set; } = TodoStatus.Pending;
    }
}
