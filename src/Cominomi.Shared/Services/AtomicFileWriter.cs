namespace Cominomi.Shared.Services;

/// <summary>
///     Writes files atomically: write to temp file first, then rename to target.
///     Prevents data corruption from crashes or concurrent writes mid-write.
/// </summary>
public static class AtomicFileWriter
{
    /// <summary>
    ///     Atomically appends content to a file by reading existing content, appending, then writing atomically.
    ///     Creates the file if it doesn't exist.
    /// </summary>
    public static async Task AppendAsync(string targetPath, string content)
    {
        var existing = File.Exists(targetPath) ? await File.ReadAllTextAsync(targetPath) : "";
        await WriteAsync(targetPath, existing + content);
    }

    public static async Task WriteAsync(string targetPath, string content)
    {
        var dir = Path.GetDirectoryName(targetPath);
        if (dir != null)
            Directory.CreateDirectory(dir);

        var tmpPath = targetPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tmpPath, content);
            File.Move(tmpPath, targetPath, true);
        }
        finally
        {
            // Clean up temp file if move failed
            try
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
            catch
            {
                /* best-effort: temp file cleanup is non-critical */
            }
        }
    }
}