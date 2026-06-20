namespace Seoro.Shared.Services.Infrastructure;

/// <summary>
///     파일을 원자적으로 씁니다: 먼저 임시 파일에 작성한 후 대상으로 이름을 변경합니다.
///     충돌이나 동시 쓰기 중간에 발생하는 데이터 손상을 방지합니다.
/// </summary>
public static class AtomicFileWriter
{
    /// <summary>
    ///     기존 콘텐츠를 읽은 후 추가한 다음 원자적으로 작성하여 파일에 콘텐츠를 원자적으로 추가합니다.
    ///     파일이 없으면 생성합니다.
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
            // 이동 실패 시 임시 파일 정리
            try
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
            catch
            {
                /* 최선의 노력: 임시 파일 정리는 필수가 아님 */
            }
        }
    }
}