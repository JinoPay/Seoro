using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Pty.Net;

namespace Seoro.Shared.Tests;

public class TerminalServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeShellService _shellService = new();
    private readonly FakePtySpawner _spawner = new();
    private readonly TestableTerminalService _sut;

    public TerminalServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"terminal_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sut = new TestableTerminalService(_shellService, _spawner, _tempDir);
    }

    public void Dispose()
    {
        _sut.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task StartAsync_OutputIsBufferedAndRaised()
    {
        var received = new StringBuilder();
        _sut.OnOutput += (_, data) => received.Append(data);

        await _sut.StartAsync("s1", _tempDir);
        var pty = _spawner.Spawned[0];
        pty.Output.Push("hello\n");
        await WaitUntilAsync(() => received.ToString().Contains("hello"));

        Assert.Contains("hello", _sut.GetBufferedOutput("s1"));
    }

    [Fact]
    public async Task ReadError_RaisesOnErrorAndOnExited()
    {
        string? errorKey = null;
        var exitedKey = (string?)null;
        _sut.OnError += (key, _) => errorKey = key;
        _sut.OnExited += (key, _) => exitedKey = key;

        await _sut.StartAsync("s1", _tempDir);
        _spawner.Spawned[0].Output.Fail(new IOException("pipe broken"));

        await WaitUntilAsync(() => errorKey != null && exitedKey != null);
        Assert.Equal("s1", errorKey);
        Assert.Equal("s1", exitedKey);
        Assert.False(_sut.IsRunning("s1"));
    }

    [Fact]
    public async Task StopAsync_CompletesReaderWithoutObjectDisposedException()
    {
        var errors = new ConcurrentBag<string>();
        _sut.OnError += (_, msg) => errors.Add(msg);

        await _sut.StartAsync("s1", _tempDir);
        var pty = _spawner.Spawned[0];
        pty.Output.Push("data\n");

        await _sut.StopAsync("s1");

        Assert.True(pty.Killed);
        Assert.Empty(errors.Where(e => e.Contains(nameof(ObjectDisposedException))));
        Assert.False(_sut.IsRunning("s1"));
    }

    [Fact]
    public async Task StopAsync_PersistsScrollbackToDisk()
    {
        await _sut.StartAsync("s1", _tempDir);
        _spawner.Spawned[0].Output.Push("save me\n");
        await WaitUntilAsync(() => _sut.GetBufferedOutput("s1").Contains("save me"));

        await _sut.StopAsync("s1");

        var path = Path.Combine(_tempDir, "s1.terminal.txt");
        Assert.True(File.Exists(path));
        Assert.Contains("save me", await File.ReadAllTextAsync(path));
        // 라이브 세션이 없어도 디스크에서 읽어옴
        Assert.Contains("save me", _sut.GetBufferedOutput("s1"));
    }

    [Fact]
    public async Task StartAsync_PreloadsScrollbackFromDisk()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "s1.terminal.txt"), "previous session output\n");

        await _sut.StartAsync("s1", _tempDir);

        Assert.Contains("previous session output", _sut.GetBufferedOutput("s1"));
    }

    [Fact]
    public async Task StartAsync_OverLimit_EvictsLeastRecentlyUsed()
    {
        for (var i = 0; i < SeoroConstants.MaxLiveTerminals; i++)
        {
            await _sut.StartAsync($"s{i}", _tempDir);
            _spawner.Spawned[i].Output.Push($"output-{i}\n");
        }

        await WaitUntilAsync(() => _sut.GetBufferedOutput("s0").Contains("output-0"));

        // s0가 가장 오래됨 — 추가 시작 시 정리 대상
        for (var i = 1; i < SeoroConstants.MaxLiveTerminals; i++)
            _sut.NotifyAttached($"s{i}");

        await _sut.StartAsync("s-new", _tempDir);

        await WaitUntilAsync(() => _spawner.Spawned[0].Killed);
        Assert.True(_spawner.Spawned[0].Killed);
        // 정리된 세션의 스크롤백은 디스크에 보존됨
        await WaitUntilAsync(() => File.Exists(Path.Combine(_tempDir, "s0.terminal.txt")));
        Assert.Contains("output-0", await File.ReadAllTextAsync(Path.Combine(_tempDir, "s0.terminal.txt")));
    }

    [Fact]
    public async Task WriteAsync_ConcurrentWrites_AreSerialized()
    {
        await _sut.StartAsync("s1", _tempDir);
        var input = _spawner.Spawned[0].Input;
        input.WriteDelay = TimeSpan.FromMilliseconds(5);

        var tasks = Enumerable.Range(0, 10)
            .Select(i => _sut.WriteAsync("s1", $"[msg{i}]"));
        await Task.WhenAll(tasks);

        // 각 쓰기가 원자적 — 메시지가 서로 끼어들지 않음
        var written = input.Written;
        for (var i = 0; i < 10; i++)
            Assert.Contains($"[msg{i}]", written);
        Assert.Equal(10, CountOccurrences(written, "[msg"));
    }

    [Fact]
    public async Task DeleteScrollbackAsync_RemovesFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "s1.terminal.txt"), "data");

        await _sut.DeleteScrollbackAsync("s1");

        Assert.False(File.Exists(Path.Combine(_tempDir, "s1.terminal.txt")));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs)
                throw new TimeoutException("조건이 시간 내에 충족되지 않음");
            await Task.Delay(10);
        }
    }

    // --- Test doubles ---

    private sealed class TestableTerminalService(
        IShellService shellService,
        IPtySpawner spawner,
        string scrollbackDir)
        : TerminalService(shellService, spawner, NullLogger<TerminalService>.Instance)
    {
        protected override string ScrollbackDirectory => scrollbackDir;
    }

    private sealed class FakePtySpawner : IPtySpawner
    {
        public List<FakePtyConnection> Spawned { get; } = [];

        public Task<IPtyConnection> SpawnAsync(PtyOptions options, CancellationToken ct)
        {
            var conn = new FakePtyConnection();
            Spawned.Add(conn);
            return Task.FromResult<IPtyConnection>(conn);
        }
    }

    private sealed class FakePtyConnection : IPtyConnection
    {
        public FakeOutputStream Output { get; } = new();
        public FakeInputStream Input { get; } = new();
        public volatile bool Killed;

        public Stream ReaderStream => Output;
        public Stream WriterStream => Input;
        public int Pid => 12345;
        public int ExitCode => 0;
        public event EventHandler<PtyExitedEventArgs>? ProcessExited
        {
            add { }
            remove { }
        }

        public bool WaitForExit(int milliseconds) => true;

        public void Kill()
        {
            Killed = true;
            Output.Complete();
        }

        public void Resize(int cols, int rows)
        {
        }

        public void Dispose()
        {
            Output.Complete();
        }
    }

    /// <summary>Push로 데이터를 공급하는 읽기 전용 스트림. Complete()로 EOF, Fail()로 읽기 예외 시뮬레이션.</summary>
    private sealed class FakeOutputStream : Stream
    {
        private readonly SemaphoreSlim _signal = new(0);
        private readonly ConcurrentQueue<byte[]> _chunks = new();
        private readonly CancellationTokenSource _closed = new();
        private Exception? _failure;

        public void Push(string text)
        {
            _chunks.Enqueue(Encoding.UTF8.GetBytes(text));
            _signal.Release();
        }

        public void Fail(Exception ex)
        {
            _failure = ex;
            _signal.Release();
        }

        public void Complete()
        {
            try { _closed.Cancel(); } catch (ObjectDisposedException) { }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _closed.Token);
            try
            {
                await _signal.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (_closed.IsCancellationRequested)
            {
                return 0; // EOF
            }

            if (_failure != null) throw _failure;
            if (!_chunks.TryDequeue(out var chunk)) return 0;
            chunk.CopyTo(buffer);
            return chunk.Length;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>쓰인 내용을 캡처하는 쓰기 전용 스트림. WriteDelay로 느린 PTY 시뮬레이션.</summary>
    private sealed class FakeInputStream : Stream
    {
        private readonly StringBuilder _written = new();
        public TimeSpan WriteDelay { get; set; } = TimeSpan.Zero;
        public string Written
        {
            get { lock (_written) return _written.ToString(); }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            var text = Encoding.UTF8.GetString(buffer.Span);
            // 의도적으로 절반 쓰고 지연 — 락 없으면 동시 쓰기가 끼어듦
            var half = text.Length / 2;
            lock (_written) _written.Append(text[..half]);
            if (WriteDelay > TimeSpan.Zero)
                await Task.Delay(WriteDelay, ct);
            lock (_written) _written.Append(text[half..]);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FakeShellService : IShellService
    {
        private static readonly ShellInfo Shell = new("fake-shell", "", ShellType.Sh);

        public Task<List<ShellInfo>> GetAvailableShellsAsync() => Task.FromResult<List<ShellInfo>>([Shell]);
        public Task<ShellInfo> GetShellAsync() => Task.FromResult(Shell);
        public Task<ShellInfo> GetTerminalShellAsync() => Task.FromResult(Shell);
        public Task<string?> GetLoginShellPathAsync() => Task.FromResult<string?>(null);
        public Task<string?> WhichAsync(string executableName) => Task.FromResult<string?>(null);
        public void InvalidateCache() { }
    }
}
