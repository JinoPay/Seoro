namespace Seoro.Shared.Models.Git;

/// <summary>
///     Git Graph 뷰어가 사용하는 단일 커밋의 메타데이터.
///     <c>git log --format=%H..%P..%an..%ae..%aI..%D..%s</c> 한 줄에 대응.
/// </summary>
public record CommitInfo(
    string Sha,
    IReadOnlyList<string> ParentShas,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt,
    string Subject,
    IReadOnlyList<string> Refs);
