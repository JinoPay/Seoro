using Seoro.Shared.Models.Chat;
using Seoro.Shared.Services.Chat;

namespace Seoro.Shared.Tests;

public class TaskListTrackerTests
{
    [Fact]
    public void ApplyTaskCreate_AddsPendingEntry()
    {
        var tracker = new TaskListTracker();

        var applied = tracker.ApplyTaskCreate("tool-1",
            """{"subject":"변수 이름 변경","description":"...","activeForm":"이름 변경 중"}""");

        Assert.True(applied);
        var snap = tracker.ToSnapshot();
        var entry = Assert.Single(snap.Entries);
        Assert.Equal("변수 이름 변경", entry.Content);
        Assert.Equal("이름 변경 중", entry.ActiveForm);
        Assert.Equal(TodoStatus.Pending, entry.Status);
    }

    [Fact]
    public void ApplyTaskCreate_SameToolUseId_DoesNotDuplicate()
    {
        var tracker = new TaskListTracker();
        tracker.ApplyTaskCreate("tool-1", """{"subject":"A","description":""}""");
        tracker.ApplyTaskCreate("tool-1", """{"subject":"A 갱신","description":""}""");

        var snap = tracker.ToSnapshot();
        var entry = Assert.Single(snap.Entries);
        Assert.Equal("A 갱신", entry.Content);
    }

    [Fact]
    public void ApplyTaskCreate_EmptyOrInvalidInput_ReturnsFalse()
    {
        var tracker = new TaskListTracker();
        Assert.False(tracker.ApplyTaskCreate("tool-1", null));
        Assert.False(tracker.ApplyTaskCreate("tool-1", "{not json"));
        Assert.False(tracker.ApplyTaskCreate("tool-1", """{"description":"no subject"}"""));
        Assert.False(tracker.HasEntries);
    }

    [Fact]
    public void ApplyTaskUpdate_BySequentialId_UpdatesStatus()
    {
        var tracker = new TaskListTracker();
        tracker.ApplyTaskCreate("tool-1", """{"subject":"A","description":""}""");
        tracker.ApplyTaskCreate("tool-2", """{"subject":"B","description":""}""");

        Assert.True(tracker.ApplyTaskUpdate("""{"taskId":"2","status":"in_progress"}"""));
        Assert.True(tracker.ApplyTaskUpdate("""{"taskId":"1","status":"completed"}"""));

        var snap = tracker.ToSnapshot();
        Assert.Equal(TodoStatus.Completed, snap.Entries[0].Status);
        Assert.Equal(TodoStatus.InProgress, snap.Entries[1].Status);
    }

    [Fact]
    public void ApplyTaskUpdate_DeletedStatus_RemovesEntry()
    {
        var tracker = new TaskListTracker();
        tracker.ApplyTaskCreate("tool-1", """{"subject":"A","description":""}""");
        tracker.ApplyTaskCreate("tool-2", """{"subject":"B","description":""}""");

        Assert.True(tracker.ApplyTaskUpdate("""{"taskId":"1","status":"deleted"}"""));

        var snap = tracker.ToSnapshot();
        var entry = Assert.Single(snap.Entries);
        Assert.Equal("B", entry.Content);

        // 삭제 후에도 남은 항목의 ID는 유지됨
        Assert.True(tracker.ApplyTaskUpdate("""{"taskId":"2","status":"completed"}"""));
        Assert.Equal(TodoStatus.Completed, tracker.ToSnapshot().Entries[0].Status);
    }

    [Fact]
    public void ApplyTaskCreateResult_RebindsCliAssignedId()
    {
        // 앱 재시작 등으로 트래커가 리셋된 재개 세션: CLI는 #5부터 번호를 이어감
        var tracker = new TaskListTracker();
        tracker.ApplyTaskCreate("tool-1", """{"subject":"A","description":""}""");
        tracker.ApplyTaskCreateResult("tool-1", "Task #5 created successfully: A");

        Assert.False(tracker.ApplyTaskUpdate("""{"taskId":"1","status":"completed"}"""));
        Assert.True(tracker.ApplyTaskUpdate("""{"taskId":"5","status":"completed"}"""));

        // 다음 생성은 #6으로 이어짐
        tracker.ApplyTaskCreate("tool-2", """{"subject":"B","description":""}""");
        Assert.True(tracker.ApplyTaskUpdate("""{"taskId":"6","status":"in_progress"}"""));
    }

    [Fact]
    public void ApplyTaskUpdate_SubjectChange_UpdatesEntry()
    {
        var tracker = new TaskListTracker();
        tracker.ApplyTaskCreate("tool-1", """{"subject":"A","description":"","activeForm":"a"}""");

        Assert.True(tracker.ApplyTaskUpdate("""{"taskId":"1","subject":"A2","activeForm":"a2"}"""));

        var entry = Assert.Single(tracker.ToSnapshot().Entries);
        Assert.Equal("A2", entry.Content);
        Assert.Equal("a2", entry.ActiveForm);
    }

    [Fact]
    public void ResetFromTodoWrite_ReplacesTrackedTasks()
    {
        var tracker = new TaskListTracker();
        tracker.ApplyTaskCreate("tool-1", """{"subject":"old","description":""}""");

        tracker.ResetFromTodoWrite(new TodoSnapshot
        {
            Entries = [new TodoEntry("새 항목", "진행 중", TodoStatus.InProgress)],
            UpdatedAt = DateTime.UtcNow
        });

        var entry = Assert.Single(tracker.ToSnapshot().Entries);
        Assert.Equal("새 항목", entry.Content);
        Assert.Equal(TodoStatus.InProgress, entry.Status);
    }

    [Fact]
    public void RebuildFromMessages_ReplaysTaskHistory()
    {
        var tracker = new TaskListTracker();
        tracker.RebuildFromMessages([
            AssistantMessage(
                Tool("tool-1", "TaskCreate", """{"subject":"A","description":""}""", "Task #1 created successfully: A"),
                Tool("tool-2", "TaskCreate", """{"subject":"B","description":""}""", "Task #2 created successfully: B")),
            AssistantMessage(
                Tool("tool-3", "TaskUpdate", """{"taskId":"1","status":"completed"}""", ""))
        ]);

        Assert.True(tracker.RebuildAttempted);
        var snap = tracker.ToSnapshot();
        Assert.Equal(2, snap.Entries.Count);
        Assert.Equal(TodoStatus.Completed, snap.Entries[0].Status);
        Assert.Equal(TodoStatus.Pending, snap.Entries[1].Status);
    }

    [Fact]
    public void RebuildFromMessages_RebindsCliIdFromToolResult()
    {
        // 재개 세션: CLI가 #5를 부여한 뒤 그 ID로 TaskUpdate가 도착하는 히스토리
        var tracker = new TaskListTracker();
        tracker.RebuildFromMessages([
            AssistantMessage(
                Tool("tool-1", "TaskCreate", """{"subject":"A","description":""}""", "Task #5 created successfully: A"),
                Tool("tool-2", "TaskUpdate", """{"taskId":"5","status":"in_progress"}""", ""))
        ]);

        var entry = Assert.Single(tracker.ToSnapshot().Entries);
        Assert.Equal(TodoStatus.InProgress, entry.Status);
    }

    [Fact]
    public void RebuildFromMessages_TodoWriteResetsThenTaskToolsContinue()
    {
        var tracker = new TaskListTracker();
        tracker.RebuildFromMessages([
            AssistantMessage(
                Tool("tool-1", "TaskCreate", """{"subject":"old","description":""}""", "Task #1 created successfully: old")),
            AssistantMessage(
                Tool("tool-2", "TodoWrite",
                    """{"todos":[{"content":"todo A","activeForm":"doing A","status":"in_progress"}]}""", "")),
            AssistantMessage(
                Tool("tool-3", "TaskCreate", """{"subject":"new","description":""}""", "Task #2 created successfully: new"))
        ]);

        var snap = tracker.ToSnapshot();
        Assert.Equal(2, snap.Entries.Count);
        Assert.Equal("todo A", snap.Entries[0].Content);
        Assert.Equal("new", snap.Entries[1].Content);
    }

    [Fact]
    public void RebuildFromMessages_CalledTwice_DoesNotAccumulate()
    {
        var tracker = new TaskListTracker();
        List<ChatMessage> history =
        [
            AssistantMessage(
                Tool("tool-1", "TaskCreate", """{"subject":"A","description":""}""", "Task #1 created successfully: A"))
        ];

        tracker.RebuildFromMessages(history);
        tracker.RebuildFromMessages(history);

        Assert.Single(tracker.ToSnapshot().Entries);
    }

    private static ChatMessage AssistantMessage(params ToolCall[] tools) =>
        new() { Role = MessageRole.Assistant, ToolCalls = [..tools] };

    private static ToolCall Tool(string id, string name, string input, string output) =>
        new() { Id = id, Name = name, Input = input, Output = output };
}
