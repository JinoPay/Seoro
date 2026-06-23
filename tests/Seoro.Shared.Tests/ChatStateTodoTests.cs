namespace Seoro.Shared.Tests;

public class ChatStateTodoTests
{
    private static ChatState NewState() =>
        new(new ActiveSessionRegistry(), new FakeBus());

    private static ChatState NewStateWithSession(string sessionId)
    {
        var s = NewState();
        s.SetSession(new Session { Id = sessionId });
        return s;
    }

    private static TodoSnapshot Snap(int total, int completed = 0)
    {
        var entries = new List<TodoEntry>();
        for (var i = 0; i < total; i++)
        {
            var status = i < completed ? TodoStatus.Completed : TodoStatus.Pending;
            entries.Add(new TodoEntry($"task {i}", $"doing task {i}", status));
        }
        return new TodoSnapshot { Entries = entries, UpdatedAt = DateTime.UtcNow };
    }

    [Fact]
    public void UpdateTodoSnapshot_PromotesFromHiddenToChip()
    {
        var s = NewStateWithSession("s1");
        Assert.Equal(TodoFloaterVisibility.Hidden, s.TodoFloaterState);

        s.UpdateTodoSnapshot("s1", Snap(3));

        Assert.NotNull(s.CurrentTodos);
        Assert.Equal(3, s.CurrentTodos!.Total);
        Assert.Equal(TodoFloaterVisibility.Chip, s.TodoFloaterState);
    }

    [Fact]
    public void UpdateTodoSnapshot_KeepsExpandedWhenAlreadyExpanded()
    {
        var s = NewStateWithSession("s1");
        s.UpdateTodoSnapshot("s1", Snap(2));
        s.SetTodoFloaterState(TodoFloaterVisibility.Expanded);

        s.UpdateTodoSnapshot("s1", Snap(2, completed: 1));

        Assert.Equal(TodoFloaterVisibility.Expanded, s.TodoFloaterState);
        Assert.Equal(1, s.CurrentTodos!.Completed);
    }

    [Fact]
    public void UpdateTodoSnapshot_BackgroundSession_DoesNotTouchFloater()
    {
        var s = NewStateWithSession("s1");
        s.UpdateTodoSnapshot("s1", Snap(3));

        // 백그라운드 세션 s2의 스냅샷은 현재 세션 플로터에 반영되지 않음
        s.UpdateTodoSnapshot("s2", Snap(1));

        Assert.Equal(3, s.CurrentTodos!.Total);
        Assert.Equal(TodoFloaterVisibility.Chip, s.TodoFloaterState);
    }

    [Fact]
    public void Dismiss_HidesFloater_NextUpdateReshowsAsChip()
    {
        var s = NewStateWithSession("s1");
        s.UpdateTodoSnapshot("s1", Snap(2));
        s.DismissTodoFloater();
        Assert.Equal(TodoFloaterVisibility.Hidden, s.TodoFloaterState);

        s.UpdateTodoSnapshot("s1", Snap(2, completed: 1));

        Assert.Equal(TodoFloaterVisibility.Chip, s.TodoFloaterState);
    }

    [Fact]
    public void SetSession_DifferentId_ResetsTodos()
    {
        var s = NewStateWithSession("s1");
        s.UpdateTodoSnapshot("s1", Snap(3));
        Assert.NotNull(s.CurrentTodos);

        s.SetSession(new Session { Id = "s2" });

        Assert.Null(s.CurrentTodos);
        Assert.Equal(TodoFloaterVisibility.Hidden, s.TodoFloaterState);
    }

    [Fact]
    public void SetSession_SameId_KeepsTodos()
    {
        var s = NewState();
        var session = new Session { Id = "s1" };
        s.SetSession(session);
        s.UpdateTodoSnapshot("s1", Snap(2));

        s.SetSession(session);

        Assert.NotNull(s.CurrentTodos);
        Assert.Equal(TodoFloaterVisibility.Chip, s.TodoFloaterState);
    }

    [Fact]
    public void SetSession_TrackerWithEntries_RestoresFloater()
    {
        var s = NewState();
        s.GetTaskTracker("s1").ApplyTaskCreate("tool-1", """{"subject":"복원 항목","description":""}""");

        s.SetSession(new Session { Id = "s1" });

        var entry = Assert.Single(s.CurrentTodos!.Entries);
        Assert.Equal("복원 항목", entry.Content);
        Assert.Equal(TodoFloaterVisibility.Chip, s.TodoFloaterState);
    }

    [Fact]
    public void SetSession_EmptyTracker_RebuildsFromMessageHistory()
    {
        // 앱 재시작 후 시나리오: 트래커는 비어 있고 메시지 히스토리만 존재
        var s = NewState();
        var session = new Session
        {
            Id = "s1",
            Messages =
            [
                new ChatMessage
                {
                    Role = MessageRole.Assistant,
                    ToolCalls =
                    [
                        new ToolCall
                        {
                            Id = "tool-1", Name = "TaskCreate",
                            Input = """{"subject":"히스토리 항목","description":""}""",
                            Output = "Task #1 created successfully: 히스토리 항목"
                        },
                        new ToolCall
                        {
                            Id = "tool-2", Name = "TaskUpdate",
                            Input = """{"taskId":"1","status":"completed"}"""
                        }
                    ]
                }
            ]
        };

        s.SetSession(session);

        var entry = Assert.Single(s.CurrentTodos!.Entries);
        Assert.Equal("히스토리 항목", entry.Content);
        Assert.Equal(TodoStatus.Completed, entry.Status);
        Assert.Equal(TodoFloaterVisibility.Chip, s.TodoFloaterState);
    }

    private sealed class FakeBus : IEventBus
    {
        public event Action? OnAny;
        public void Publish<T>(T evt) where T : DomainEvent => OnAny?.Invoke();
        public IDisposable Subscribe<T>(Action<T> handler) where T : DomainEvent => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
