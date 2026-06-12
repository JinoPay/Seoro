using System.Text;

namespace Seoro.Shared.Services.Infrastructure;

/// <summary>
///     터미널 raw ANSI 출력을 메모리에 누적하는 바운디드 버퍼.
///     세션 전환 시 xterm replay와 앱 재시작 시 스크롤백 복원의 단일 소스.
///     상한 초과 시 앞쪽에서 줄바꿈 경계로 트림한다.
/// </summary>
public sealed class TerminalScrollbackBuffer(int maxChars = TerminalScrollbackBuffer.DefaultMaxChars)
{
    public const int DefaultMaxChars = 512 * 1024;

    private readonly object _lock = new();
    private readonly StringBuilder _buffer = new();

    /// <summary>마지막 MarkSaved 이후 내용이 추가되었는지 여부.</summary>
    public bool IsDirty { get; private set; }

    public void Append(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return;

        lock (_lock)
        {
            _buffer.Append(chunk);
            IsDirty = true;

            if (_buffer.Length <= maxChars) return;

            // 초과분 + 다음 줄바꿈까지 앞에서 제거 (줄 중간에서 끊지 않음)
            var excess = _buffer.Length - maxChars;
            var cut = excess;
            while (cut < _buffer.Length && _buffer[cut] != '\n')
                cut++;
            _buffer.Remove(0, Math.Min(cut + 1, _buffer.Length));
        }
    }

    public string Snapshot()
    {
        lock (_lock)
        {
            return _buffer.ToString();
        }
    }

    /// <summary>디스크 저장 완료 후 호출 — dirty 플래그 해제.</summary>
    public void MarkSaved()
    {
        lock (_lock)
        {
            IsDirty = false;
        }
    }
}
