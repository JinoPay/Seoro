namespace Seoro.Shared.Services.Git;

/// <summary>
///     git 브랜치 ref 문자열 정규화 유틸리티.
///     <c>"main"</c> / <c>"origin/main"</c> / <c>"refs/heads/main"</c> / <c>"refs/remotes/origin/main"</c> 등
///     동일한 브랜치를 가리키는 다양한 표기를 하나의 짧은 이름으로 정규화한다.
///     β 버그 #2(단순 문자열 매치 실패로 detached HEAD 머지) 재발 방지를 위한 단일 책임 함수.
///     — <see cref="MatchInGroups"/>는 사용자가 저장한 BaseBranch 를 현재 브랜치 그룹과 매칭해
///     <c>git checkout</c> 이 로컬 브랜치를 만들 수 있는 형태로 돌려준다.
/// </summary>
public static class BranchRefNormalizer
{
    private const string RefsHeadsPrefix = "refs/heads/";
    private const string RefsRemotesPrefix = "refs/remotes/";

    /// <summary>
    ///     git ref 접두사(<c>refs/heads/</c>, <c>refs/remotes/&lt;remote&gt;/</c>)와 리모트 접두사(<c>&lt;remote&gt;/</c>)를 제거해
    ///     순수 브랜치 이름만 남긴다.
    ///     예: <c>"refs/remotes/origin/feature/x"</c> → <c>"feature/x"</c>
    /// </summary>
    /// <param name="branch">정규화할 브랜치 ref. null/빈 값은 그대로 돌려준다.</param>
    /// <param name="knownRemotes">
    ///     알려진 리모트 이름 목록 (예: <c>["origin", "upstream"]</c>). 비어 있으면 origin 만 고려.
    ///     "origin/feature" 같은 짧은 형식을 정규화하려면 리모트 이름을 알아야 한다.
    /// </param>
    public static string Normalize(string? branch, IEnumerable<string>? knownRemotes = null)
    {
        if (string.IsNullOrWhiteSpace(branch))
            return string.Empty;

        var value = branch.Trim();

        // refs/heads/* → *
        if (value.StartsWith(RefsHeadsPrefix, StringComparison.Ordinal))
            return value[RefsHeadsPrefix.Length..];

        // refs/remotes/<remote>/* → *
        if (value.StartsWith(RefsRemotesPrefix, StringComparison.Ordinal))
        {
            var tail = value[RefsRemotesPrefix.Length..];
            var slash = tail.IndexOf('/');
            if (slash > 0)
                return tail[(slash + 1)..];
        }

        // <remote>/* 형식 — 알려진 리모트로 시작하면 리모트 접두사 제거
        var remotes = knownRemotes?.ToArray() ?? ["origin"];
        foreach (var remote in remotes)
        {
            var prefix = remote + "/";
            if (value.StartsWith(prefix, StringComparison.Ordinal))
                return value[prefix.Length..];
        }

        return value;
    }

    /// <summary>
    ///     저장된 BaseBranch(예: <c>"origin/main"</c>)를 현재 브랜치 그룹과 매칭해
    ///     <c>git checkout</c>이 로컬 브랜치를 만들 수 있는 정규 형태로 돌려준다.
    ///     로컬에 같은 이름이 있으면 <c>"main"</c>, 리모트에만 있으면 <c>"origin/main"</c> 유지.
    ///     매칭 실패 시 null.
    /// </summary>
    /// <param name="stored">저장되어 있던 브랜치 이름 (예: <c>Session.Git.BaseBranch</c>).</param>
    /// <param name="groups">현재 저장소의 브랜치 그룹 목록. 일반적으로 "origin", "로컬", 기타 리모트 순.</param>
    public static string? MatchInGroups(string? stored, IReadOnlyList<BranchGroup> groups)
    {
        if (string.IsNullOrWhiteSpace(stored) || groups.Count == 0)
            return null;

        var remoteNames = groups
            .Select(g => g.Name)
            .Where(name => !string.Equals(name, "로컬", StringComparison.Ordinal))
            .ToArray();

        var normalizedStored = Normalize(stored, remoteNames);

        // 1) 로컬 그룹에 동일 이름이 있으면 로컬로 매칭 (체크아웃이 detached HEAD 로 빠지지 않음)
        var localGroup = groups.FirstOrDefault(g => string.Equals(g.Name, "로컬", StringComparison.Ordinal));
        if (localGroup != null)
        {
            var localHit = localGroup.Branches
                .FirstOrDefault(b => string.Equals(Normalize(b, remoteNames), normalizedStored, StringComparison.Ordinal));
            if (localHit != null)
                return localHit;
        }

        // 2) origin 그룹 (리모트 전용 브랜치) — checkout 시 git 이 자동으로 로컬 트래킹 브랜치 생성
        var originGroup = groups.FirstOrDefault(g => string.Equals(g.Name, "origin", StringComparison.Ordinal));
        if (originGroup != null)
        {
            var originHit = originGroup.Branches
                .FirstOrDefault(b => string.Equals(Normalize(b, remoteNames), normalizedStored, StringComparison.Ordinal));
            if (originHit != null)
                return originHit;
        }

        // 3) 그 외 리모트
        foreach (var group in groups)
        {
            if (string.Equals(group.Name, "로컬", StringComparison.Ordinal) ||
                string.Equals(group.Name, "origin", StringComparison.Ordinal))
                continue;

            var hit = group.Branches
                .FirstOrDefault(b => string.Equals(Normalize(b, remoteNames), normalizedStored, StringComparison.Ordinal));
            if (hit != null)
                return hit;
        }

        return null;
    }
}
