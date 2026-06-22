using Microsoft.Extensions.Logging.Abstractions;

namespace Seoro.Shared.Tests;

public class CorruptedFileQuarantineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<string> _quarantinedToCleanup = [];

    public CorruptedFileQuarantineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seoro-quarantine-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        // 실제 AppData 격리 디렉터리에 생성된 파일 정리
        foreach (var q in _quarantinedToCleanup)
            if (File.Exists(q))
                File.Delete(q);
    }

    [Fact]
    public void Quarantine_MovesFile_AndRemovesOriginal()
    {
        var src = Path.Combine(_tempDir, "broken.json");
        File.WriteAllText(src, "{ corrupt");

        var dest = CorruptedFileQuarantine.Quarantine(src, NullLogger.Instance);

        Assert.NotNull(dest);
        _quarantinedToCleanup.Add(dest!);
        Assert.False(File.Exists(src), "원본은 이동 후 남아있으면 안 된다");
        Assert.True(File.Exists(dest), "격리본이 존재해야 한다");
        Assert.Equal("{ corrupt", File.ReadAllText(dest!));
    }

    [Fact]
    public void Quarantine_PreservesOriginalFileNameInDest()
    {
        var src = Path.Combine(_tempDir, "session-abc.json");
        File.WriteAllText(src, "x");

        var dest = CorruptedFileQuarantine.Quarantine(src, NullLogger.Instance);

        Assert.NotNull(dest);
        _quarantinedToCleanup.Add(dest!);
        Assert.EndsWith("session-abc.json", dest);
    }

    [Fact]
    public void Quarantine_NonExistentFile_ReturnsNull()
    {
        var src = Path.Combine(_tempDir, "does-not-exist.json");
        var dest = CorruptedFileQuarantine.Quarantine(src, NullLogger.Instance);
        Assert.Null(dest);
    }

    [Fact]
    public void Quarantine_TwoFilesSameName_DoNotOverwrite()
    {
        var src1 = Path.Combine(_tempDir, "dup.json");
        File.WriteAllText(src1, "first");
        var dest1 = CorruptedFileQuarantine.Quarantine(src1, NullLogger.Instance);

        // 동일 이름 파일을 다시 손상시켜 격리 — 타임스탬프 접두사로 충돌하지 않아야 함
        File.WriteAllText(src1, "second");
        var dest2 = CorruptedFileQuarantine.Quarantine(src1, NullLogger.Instance);

        Assert.NotNull(dest1);
        Assert.NotNull(dest2);
        _quarantinedToCleanup.Add(dest1!);
        _quarantinedToCleanup.Add(dest2!);
        Assert.NotEqual(dest1, dest2);
        Assert.True(File.Exists(dest1));
        Assert.True(File.Exists(dest2));
    }
}
