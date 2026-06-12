using System.Text.Json;

namespace Seoro.Shared.Services.Claude.Bidirectional;

/// <summary>
///     stdout에서 수신한 control_request의 파싱 결과.
///     비공식 형식이므로 원본 <see cref="Request" />를 보존해 고급 처리에 사용한다.
/// </summary>
internal sealed record ClaudeControlRequest(string RequestId, string Subtype, JsonElement Request);
