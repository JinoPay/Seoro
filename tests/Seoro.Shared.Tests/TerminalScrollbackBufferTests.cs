namespace Seoro.Shared.Tests;

public class TerminalScrollbackBufferTests
{
    [Fact]
    public void Append_AccumulatesContent()
    {
        var buffer = new TerminalScrollbackBuffer();
        buffer.Append("hello ");
        buffer.Append("world");

        Assert.Equal("hello world", buffer.Snapshot());
    }

    [Fact]
    public void Append_SetsDirtyFlag()
    {
        var buffer = new TerminalScrollbackBuffer();
        Assert.False(buffer.IsDirty);

        buffer.Append("data");
        Assert.True(buffer.IsDirty);

        buffer.MarkSaved();
        Assert.False(buffer.IsDirty);

        buffer.Append("more");
        Assert.True(buffer.IsDirty);
    }

    [Fact]
    public void Append_EmptyOrNull_DoesNothing()
    {
        var buffer = new TerminalScrollbackBuffer();
        buffer.Append("");

        Assert.Equal("", buffer.Snapshot());
        Assert.False(buffer.IsDirty);
    }

    [Fact]
    public void Append_OverLimit_TrimsFromFrontAtNewlineBoundary()
    {
        var buffer = new TerminalScrollbackBuffer(maxChars: 20);
        buffer.Append("line1\nline2\nline3\nline4\n");

        var snapshot = buffer.Snapshot();
        Assert.True(snapshot.Length <= 20);
        // 트림은 줄바꿈 경계에서 일어남 — 잘린 줄 조각이 남지 않음
        Assert.StartsWith("line", snapshot);
        Assert.EndsWith("line4\n", snapshot);
    }

    [Fact]
    public void Append_SingleChunkLargerThanLimit_KeepsTail()
    {
        var buffer = new TerminalScrollbackBuffer(maxChars: 10);
        buffer.Append(new string('a', 50) + "\n" + "tail\n");

        var snapshot = buffer.Snapshot();
        Assert.Equal("tail\n", snapshot);
    }

    [Fact]
    public void Append_NoNewlineInOverflow_DropsEverythingBeforeLimit()
    {
        // 줄바꿈이 전혀 없으면 전체가 한 줄 — 경계를 못 찾고 모두 제거됨
        var buffer = new TerminalScrollbackBuffer(maxChars: 10);
        buffer.Append(new string('x', 30));

        Assert.Equal("", buffer.Snapshot());
    }

    [Fact]
    public async Task Append_ConcurrentWrites_DoesNotCorrupt()
    {
        var buffer = new TerminalScrollbackBuffer();
        var tasks = Enumerable.Range(0, 8)
            .Select(i => Task.Run(() =>
            {
                for (var j = 0; j < 100; j++)
                    buffer.Append($"w{i}-{j}\n");
            }));

        await Task.WhenAll(tasks);

        var lines = buffer.Snapshot().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(800, lines.Length);
    }
}
