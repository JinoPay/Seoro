using Cominomi.Shared.Services;

namespace Cominomi.Shared.Tests;

public class ContextServiceGitignoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ContextService _sut = new();

    public ContextServiceGitignoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cominomi-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task EnsureContext_AddsGitignoreEntry_WhenNotPresent()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "bin/\nobj/\n");

        await _sut.EnsureContextDirectoryAsync(_tempDir);

        var lines = await File.ReadAllLinesAsync(Path.Combine(_tempDir, ".gitignore"));
        Assert.Contains(".context/", lines.Select(l => l.Trim()));
    }

    [Fact]
    public async Task EnsureContext_DoesNotDuplicate_WhenExactLineExists()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "bin/\n.context/\nobj/\n");

        await _sut.EnsureContextDirectoryAsync(_tempDir);

        var content = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".gitignore"));
        var count = content.Split('\n').Count(l => l.Trim() == ".context/");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task EnsureContext_AddsEntry_WhenOnlySubstringMatch()
    {
        // ".context/" appears as substring in a comment but not as its own line
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "# ignore .context/ stuff\nbin/\n");

        await _sut.EnsureContextDirectoryAsync(_tempDir);

        var lines = await File.ReadAllLinesAsync(Path.Combine(_tempDir, ".gitignore"));
        // Should have added a standalone .context/ line
        Assert.Contains(".context/", lines.Select(l => l.Trim()));
        // The comment line should still be there
        Assert.Contains("# ignore .context/ stuff", lines);
    }

    [Fact]
    public async Task EnsureContext_AddsEntry_WhenPartOfAnotherPath()
    {
        // "my.context/" contains ".context/" as substring — old code would skip adding
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), "my.context/\nbin/\n");

        await _sut.EnsureContextDirectoryAsync(_tempDir);

        var lines = await File.ReadAllLinesAsync(Path.Combine(_tempDir, ".gitignore"));
        Assert.Contains(".context/", lines.Select(l => l.Trim()));
    }
}
