using System.Text;

namespace Seoro.Shared.Tests;

public class AtomicFileWriterTests : IDisposable
{
    private readonly string _tempDir;

    public AtomicFileWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seoro-atomic-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task WriteAsync_Text_WritesContent()
    {
        var path = Path.Combine(_tempDir, "a.txt");
        await AtomicFileWriter.WriteAsync(path, "hello");
        Assert.Equal("hello", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteAsync_Bytes_WritesContent()
    {
        var path = Path.Combine(_tempDir, "a.bin");
        var data = Encoding.UTF8.GetBytes("바이너리 콘텐츠");
        await AtomicFileWriter.WriteAsync(path, data);
        Assert.Equal(data, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task WriteAsync_Overwrites_ExistingFile()
    {
        var path = Path.Combine(_tempDir, "a.txt");
        await File.WriteAllTextAsync(path, "old");
        await AtomicFileWriter.WriteAsync(path, "new");
        Assert.Equal("new", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteAsync_CreatesMissingDirectory()
    {
        var path = Path.Combine(_tempDir, "nested", "deep", "a.txt");
        await AtomicFileWriter.WriteAsync(path, "x");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task WriteAsync_LeavesNoTempFiles()
    {
        var path = Path.Combine(_tempDir, "a.txt");
        await AtomicFileWriter.WriteAsync(path, "data");
        var leftovers = Directory.GetFiles(_tempDir, "*.tmp");
        Assert.Empty(leftovers);
    }

    [Fact]
    public async Task WriteAsync_ConcurrentWritesToSameTarget_NoCorruption()
    {
        // 동시 쓰기가 임시 파일을 공유하지 않으므로 최종 내용은 쓴 값 중 하나여야 하고
        // 부분적으로 섞이거나 잘리지 않아야 한다.
        var path = Path.Combine(_tempDir, "concurrent.txt");
        var candidates = Enumerable.Range(0, 20).Select(i => new string((char)('a' + i % 26), 5000)).ToArray();

        var tasks = candidates.Select(c => AtomicFileWriter.WriteAsync(path, c)).ToArray();
        await Task.WhenAll(tasks);

        var final = await File.ReadAllTextAsync(path);
        Assert.Contains(final, candidates);
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }
}
