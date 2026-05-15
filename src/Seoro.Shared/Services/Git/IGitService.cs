
namespace Seoro.Shared.Services.Git;

public record GitResult(bool Success, string Output, string Error);

public interface IGitService
{
    Task<(int Additions, int Deletions)> GetDiffStatAsync(string workingDir, string baseBranch,
        CancellationToken ct = default);

    Task<(int Ahead, int Behind)> GetAheadBehindAsync(string workingDir, CancellationToken ct = default);

    /// <summary>
    ///     지정한 원격의 URL을 조회한다 (<c>git remote get-url</c>). 원격이 없거나 git 저장소가 아니면 null.
    /// </summary>
    Task<string?> GetRemoteUrlAsync(string repoDir, string remoteName = "origin",
        CancellationToken ct = default);

    /// <summary>
    /// <summary>
    ///     작업 디렉터리에 해결되지 않은 머지 충돌이 있는지 판정한다.
    ///     <c>.git/MERGE_HEAD</c> 파일 존재 + <c>git status --porcelain</c>의 UU/AA/DD/AU/UA/DU/UD 마커.
    ///     상태는 저장하지 않고 매 호출마다 git을 직접 질의한다.
    /// </summary>
    Task<bool> HasUnresolvedConflictsAsync(string workingDir, CancellationToken ct = default);

    /// <summary>
    ///     원격에서 타겟 브랜치를 fetch한 뒤 source 브랜치와의 ahead/behind를 계산한다.
    ///     fetch 실패 시 null — 호출자는 "오프라인 — 재시도/취소" Hard block 다이얼로그를 띄워야 한다.
    /// </summary>
    Task<(int Ahead, int Behind)?> FetchAndCompareAsync(string repoDir,
        string sourceRef, string targetRef, CancellationToken ct = default);

    /// <summary>
    ///     비파괴 머지 시뮬레이션 (<c>git merge-tree --write-tree</c>).
    ///     실제 ref/working tree를 건드리지 않고 "이 머지가 충돌할까?"를 계산한다.
    ///     <see cref="MergeStatusService"/>가 라이브 상태 추적에 사용. git 2.38+ 필요.
    /// </summary>
    Task<MergeSimulationResult> SimulateMergeAsync(string repoDir,
        string sourceRef, string targetRef, CancellationToken ct = default);

    /// <summary>
    ///     <c>git status --porcelain</c>으로 워크트리의 미커밋/untracked 변경 파일 목록을 돌려준다.
    ///     MergeStatusService가 UncommittedDirty 경고를 생성할 때 사용.
    /// </summary>
    Task<List<string>> GetUncommittedChangesAsync(string workingDir, CancellationToken ct = default);

    /// <summary>
    ///     임시 클론(<c>git clone --no-hardlinks</c>)을 통해 워크트리 브랜치를 타겟 브랜치에 squash merge 한다.
    ///     <list type="number">
    ///         <item><description><see cref="AppPaths.MergeStaging"/> 하위에 임시 디렉터리 생성</description></item>
    ///         <item><description><c>git clone --no-hardlinks &lt;mainRepo&gt; &lt;temp&gt;</c> — temp 의 origin 은 로컬 메인 레포</description></item>
    ///         <item><description>temp 에서 타겟 브랜치 체크아웃 후 소스 브랜치를 fetch</description></item>
    ///         <item><description><c>git merge --squash</c> → 충돌 감지(Alt A) 시 abort + 임시 디렉터리 삭제</description></item>
    ///         <item><description>성공 시 커밋 + <c>git push origin &lt;target&gt;</c> 로 메인 레포 ref 갱신</description></item>
    ///         <item><description>finally 블록에서 임시 디렉터리 정리 (best-effort)</description></item>
    ///     </list>
    ///     메인 레포의 working tree 는 만지지 않으므로 워크트리 격리 규칙을 위배하지 않는다.
    /// </summary>
    Task<SquashMergeResult> SquashMergeViaTempCloneAsync(
        string mainRepoDir,
        string sourceWorktreePath,
        string sourceBranchName,
        string targetBranchName,
        string commitMessage,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    ///     <see cref="ListAllBranchesGroupedAsync"/>의 30초 캐시를 수동으로 무효화한다.
    ///     MergeToolbar 의 새로고침 버튼(β 버그 #5 해결)에서 호출.
    /// </summary>
    Task InvalidateBranchCacheAsync(string repoDir);

    Task<bool> IsGitRepoAsync(string path);
    Task<DiffSummary> GetDiffSummaryAsync(string workingDir, string baseBranch, CancellationToken ct = default);

    /// <summary>
    ///     워킹 트리의 현재 상태(HEAD 기준)를 staged/unstaged/untracked 로 분리해서 돌려준다.
    ///     VS Code 스타일 Git 패널이 사용한다. <c>git diff --cached --numstat</c> +
    ///     <c>git diff --numstat</c> + <c>git status --porcelain</c> + untracked 목록을 합성.
    ///     각 <see cref="FileDiff"/> 의 <see cref="FileDiff.Staging"/> / <see cref="FileDiff.Additions"/> /
    ///     <see cref="FileDiff.StagedAdditions"/> 가 채워진다.
    /// </summary>
    Task<DiffSummary> GetWorkingTreeStatusAsync(string workingDir, CancellationToken ct = default);

    Task<GitResult> AddWorktreeAsync(string repoDir, string worktreePath, string branchName, string baseBranch,
        CancellationToken ct = default);

    Task<GitResult> CheckoutFilesAsync(string workingDir, IEnumerable<string> relativePaths,
        CancellationToken ct = default);

    Task<GitResult> CloneAsync(string url, string targetDir, IProgress<string>? progress = null,
        CancellationToken ct = default);

    Task<GitResult> CommitAsync(string workingDir, string message, CancellationToken ct = default);

    Task<GitResult> DeleteBranchAsync(string repoDir, string branchName, CancellationToken ct = default);
    Task<GitResult> FetchAllAsync(string repoDir, CancellationToken ct = default);
    Task<GitResult> FetchAsync(string repoDir, CancellationToken ct = default);
    Task<GitResult> InitAsync(string path, CancellationToken ct = default);
    Task<GitResult> RemoveWorktreeAsync(string repoDir, string worktreePath, CancellationToken ct = default);

    Task<GitResult> RenameBranchAsync(string workingDir, string oldName, string newName,
        CancellationToken ct = default);

    Task<GitResult> StageAllAsync(string workingDir, CancellationToken ct = default);

    /// <summary>
    ///     단일 파일을 인덱스에 stage 한다 (<c>git add -- &lt;path&gt;</c>).
    ///     상대 경로는 워크트리 루트 기준. untracked 파일도 추가 가능.
    /// </summary>
    Task<GitResult> StageFileAsync(string workingDir, string relativePath, CancellationToken ct = default);

    /// <summary>
    ///     인덱스에서 단일 파일을 unstage 한다 (<c>git restore --staged -- &lt;path&gt;</c>).
    ///     워킹 트리는 건드리지 않는다. fallback: <c>git reset HEAD -- &lt;path&gt;</c>.
    /// </summary>
    Task<GitResult> UnstageFileAsync(string workingDir, string relativePath, CancellationToken ct = default);

    /// <summary>
    ///     단일 파일의 unstaged 변경을 폐기한다.
    ///     - 추적 중: <c>git checkout -- &lt;path&gt;</c> (CheckoutFilesAsync 위임)
    ///     - untracked: 파일 시스템 삭제 (워크트리 경계 검증)
    /// </summary>
    Task<GitResult> DiscardFileAsync(string workingDir, string relativePath, CancellationToken ct = default);

    /// <summary>
    ///     현재 브랜치를 origin에 push 한다 (<c>git push</c> 또는 <c>git push --set-upstream origin &lt;branch&gt;</c>).
    ///     upstream 미설정 시 자동으로 <paramref name="setUpstream"/>를 true로 호출하는 것은 호출자 책임.
    /// </summary>
    Task<GitResult> PushAsync(string workingDir, bool setUpstream = false, CancellationToken ct = default);

    /// <summary>
    ///     원격에서 pull 한다.
    ///     기본 전략은 rebase (<c>git pull --rebase</c>) — Seoro 워크트리 격리 규칙과 호환.
    ///     충돌 시 GitResult.Success=false, <see cref="ConflictWatcherService"/>가 별도로 감지.
    /// </summary>
    Task<GitResult> PullAsync(string workingDir, bool rebase = true, CancellationToken ct = default);

    Task<List<BranchGroup>> ListAllBranchesGroupedAsync(string repoDir);
    Task<List<string>> GetChangedFilesAsync(string workingDir, string baseBranch, CancellationToken ct = default);

    Task<List<string>> GetStatusPorcelainAsync(string workingDir, CancellationToken ct = default);
    Task<List<string>> ListTrackedFilesAsync(string workingDir, CancellationToken ct = default);
    Task<string?> DetectDefaultBranchAsync(string repoDir);
    Task<string?> GetCurrentBranchAsync(string repoDir);
    Task<string?> ResolveCommitHashAsync(string repoDir, string refName, CancellationToken ct = default);

    Task<string[]> ReadBaseFileLinesAsync(string workingDir, string baseBranch, string relativePath, int startLine,
        int endLine, CancellationToken ct = default);

    Task<string[]> ReadFileLinesAsync(string workingDir, string relativePath, int startLine, int endLine,
        CancellationToken ct = default);

    Task<string> GetNameStatusAsync(string workingDir, string baseBranch, CancellationToken ct = default);
    Task<string> ReadFileAsync(string workingDir, string relativePath, CancellationToken ct = default);

    /// <summary>
    ///     워크스페이스 경계 검증 후 파일을 원자적으로 쓴다 (<see cref="AtomicFileWriter"/> 사용).
    ///     <paramref name="relativePath"/>가 <paramref name="workingDir"/> 밖을 가리키면 <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    Task WriteFileAsync(string workingDir, string relativePath, string content, CancellationToken ct = default);

    /// <summary>
    ///     워크스페이스 경계 검증 후 파일의 마지막 쓰기 시각을 UTC로 반환한다. 파일이 없으면 null.
    /// </summary>
    Task<DateTime?> GetFileMtimeUtcAsync(string workingDir, string relativePath);

    /// <summary>
    ///     <c>git log --all --format=…</c>로 로컬+리모트 모든 ref의 커밋 히스토리를 가져온다.
    ///     각 커밋의 부모 SHA들과 ref 데코를 포함해 Git Graph 뷰어가 lane을 그리는 데 사용.
    ///     출력은 NUL/RS 구분자로 안전하게 파싱한다(메시지에 개행이 있어도 안전).
    /// </summary>
    Task<IReadOnlyList<CommitInfo>> GetCommitHistoryAsync(string repoDir, int limit = 500,
        CancellationToken ct = default);

    /// <summary>
    ///     단일 커밋의 변경 파일 목록을 <c>git diff-tree --name-status -M</c>로 가져온다.
    ///     머지 커밋은 첫 부모(parent #1) 기준 단순화. 결과는 <c>(Path, Status)</c> 튜플 — Status는
    ///     A/M/D/R/C 등 git porcelain 코드.
    /// </summary>
    Task<IReadOnlyList<(string Path, string Status)>> GetCommitChangedFilesAsync(string repoDir, string sha,
        CancellationToken ct = default);

    /// <summary>
    ///     특정 커밋의 단일 파일 diff을 <see cref="FileDiff"/>로 반환한다(<c>git show {sha} -- {path}</c>).
    ///     <see cref="FileDiff.UnifiedDiff"/>에 raw unified diff가 담기며, <see cref="FileDiffView"/>가
    ///     <c>DiffParser</c>로 파싱한다. 파일이 없거나 binary면 IsBinary가 true.
    /// </summary>
    Task<FileDiff?> GetCommitFileDiffAsync(string repoDir, string sha, string filePath,
        CancellationToken ct = default);
}