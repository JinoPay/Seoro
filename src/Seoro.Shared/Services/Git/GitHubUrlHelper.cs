using System.Text.RegularExpressions;

namespace Seoro.Shared.Services.Git;

/// <summary>
///     GitHub 원격 URL 파싱 및 GitHub 웹 URL 생성 유틸리티.
///     순수 함수 모음 — 네트워크 호출·DI 없음. 단위 테스트 용이성을 위해 static 클래스로 분리.
///     1단계에서는 GitHub만 식별하고 나머지(GitLab 등)는 <see cref="RemoteMode.Other"/>로 돌려준다 (폐기 결정).
/// </summary>
public static partial class GitHubUrlHelper
{
    /// <summary>
    ///     GitHub URL에서 owner/repo를 파싱한다. GitHub이 아니면 null.
    ///     지원 형식:
    ///     <list type="bullet">
    ///         <item><description><c>https://github.com/OWNER/REPO</c></description></item>
    ///         <item><description><c>https://github.com/OWNER/REPO.git</c></description></item>
    ///         <item><description><c>git@github.com:OWNER/REPO.git</c></description></item>
    ///         <item><description><c>ssh://git@github.com/OWNER/REPO.git</c></description></item>
    ///     </list>
    /// </summary>
    public static (string Owner, string Repo)? TryParseGitHub(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
            return null;

        var trimmed = remoteUrl.Trim();

        // https://github.com/owner/repo(.git)?
        var https = HttpsGithubRegex().Match(trimmed);
        if (https.Success)
            return (https.Groups[1].Value, StripGitSuffix(https.Groups[2].Value));

        // git@github.com:owner/repo(.git)?
        var scp = ScpGithubRegex().Match(trimmed);
        if (scp.Success)
            return (scp.Groups[1].Value, StripGitSuffix(scp.Groups[2].Value));

        // ssh://git@github.com/owner/repo(.git)?
        var ssh = SshGithubRegex().Match(trimmed);
        if (ssh.Success)
            return (ssh.Groups[1].Value, StripGitSuffix(ssh.Groups[2].Value));

        return null;
    }

    /// <summary>
    ///     원격 URL을 <see cref="RemoteInfo"/>로 빌드한다.
    ///     null/빈 URL → <see cref="RemoteInfo.None"/>, GitHub → <see cref="RemoteMode.GitHub"/>, 그 외 → <see cref="RemoteMode.Other"/>.
    /// </summary>
    public static RemoteInfo BuildRemoteInfo(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
            return RemoteInfo.None;

        var trimmed = remoteUrl.Trim();
        var parsed = TryParseGitHub(trimmed);
        if (parsed != null)
            return new RemoteInfo(RemoteMode.GitHub, trimmed, parsed.Value.Owner, parsed.Value.Repo);

        return new RemoteInfo(RemoteMode.Other, trimmed, null, null);
    }

    /// <summary>
    ///     GitHub compare URL을 만든다. base/head 는 <see cref="BranchRefNormalizer.Normalize"/>로 정규화 후 사용.
    ///     예: <c>https://github.com/owner/repo/compare/main...feature/x</c>
    /// </summary>
    public static string BuildCompareUrl(string owner, string repo, string baseBranch, string headBranch)
    {
        var normalizedBase = BranchRefNormalizer.Normalize(baseBranch);
        var normalizedHead = BranchRefNormalizer.Normalize(headBranch);
        // URL-encode는 GitHub 브랜치명에서 거의 필요 없지만(슬래시는 허용) 공백 등은 방어적으로 처리
        return $"https://github.com/{owner}/{repo}/compare/{Uri.EscapeDataString(normalizedBase).Replace("%2F", "/")}...{Uri.EscapeDataString(normalizedHead).Replace("%2F", "/")}";
    }

    /// <summary>
    ///     로그 출력 시 URL 내 자격 증명(토큰·패스워드)을 마스킹한다.
    ///     예: <c>https://user:TOKEN@github.com/org/repo</c> → <c>https://user:***@github.com/org/repo</c>
    /// </summary>
    public static string MaskCredentials(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;

        return CredentialRegex().Replace(url, m => $"{m.Groups[1].Value}:***@");
    }

    private static string StripGitSuffix(string repo) =>
        repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repo[..^4] : repo;

    // https://github.com/OWNER/REPO(.git)?
    [GeneratedRegex(@"^https?://github\.com/([^/\s]+)/([^/\s?#]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex HttpsGithubRegex();

    // git@github.com:OWNER/REPO(.git)?
    [GeneratedRegex(@"^git@github\.com:([^/\s]+)/([^/\s?#]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex ScpGithubRegex();

    // ssh://git@github.com/OWNER/REPO(.git)?
    [GeneratedRegex(@"^ssh://git@github\.com/([^/\s]+)/([^/\s?#]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex SshGithubRegex();

    // https://user:password@host 형태의 자격증명 마스킹
    [GeneratedRegex(@"(https?://[^:@/\s]+):[^@\s]+@")]
    private static partial Regex CredentialRegex();
}
