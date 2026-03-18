# Cominomi 해부 문서 (Anatomy)

> 이 문서는 Cominomi의 모든 시스템을 해부하여 **현재 동작**, **데이터 흐름**, **의존관계**, **빠진 것/문제점**을 기술합니다.
> 각 섹션을 가리켜 "이 부분은 이렇게 변경되어야 한다"고 지휘하는 데 사용하세요.

**코드 규모**: 129개 소스 파일, ~14,600줄 (Cominomi.Shared에 집중)
**프레임워크**: .NET 10.0, MAUI + Blazor, MudBlazor UI
**외부 도구**: Claude CLI (subprocess), Git CLI, GitHub CLI (gh)

---

## 목차

- [Part I: 기반 레이어](#part-i-기반-레이어)
  - [1. 앱 부트스트랩 & DI](#1-앱-부트스트랩--di)
  - [2. 영속화 레이어](#2-영속화-레이어-json-파일-스토리지)
  - [3. 데이터 모델](#3-데이터-모델)
- [Part II: 외부 도구 통합](#part-ii-외부-도구-통합)
  - [4. 셸 서비스 & 프로세스 실행](#4-셸-서비스--프로세스-실행)
  - [5. Git 서비스](#5-git-서비스)
  - [6. GitHub CLI 서비스](#6-github-cli-서비스)
  - [7. Claude CLI 서비스](#7-claude-cli-서비스)
- [Part III: 워크플로우 오케스트레이션](#part-iii-워크플로우-오케스트레이션)
  - [8. 세션 생명주기](#8-세션-생명주기)
  - [9. Git 워크플로우 파이프라인](#9-git-워크플로우-파이프라인)
  - [10. ChatView - 중앙 오케스트레이터](#10-chatview---중앙-오케스트레이터-핵심-문제)
- [Part IV: UI 컴포넌트 시스템](#part-iv-ui-컴포넌트-시스템)
  - [11. 레이아웃 아키텍처](#11-레이아웃-아키텍처)
  - [12. 사이드바 & 세션 관리 UI](#12-사이드바--세션-관리-ui)
  - [13. 채팅 컴포넌트](#13-채팅-컴포넌트)
- [Part V: 확장성 시스템](#part-v-확장성-시스템)
  - [14. 훅 엔진](#14-훅-엔진)
  - [15. 스킬 레지스트리](#15-스킬-레지스트리-슬래시-커맨드)
  - [16. MCP 서비스](#16-mcp-서비스)
  - [17. 컨텍스트 서비스](#17-컨텍스트-서비스)
  - [18. 메모리 서비스](#18-메모리-서비스)
- [Part VI: 지원 시스템](#part-vi-지원-시스템)
  - [19. 스포트라이트 서비스](#19-스포트라이트-서비스)
  - [20. 사용량 추적 & 비용 계산](#20-사용량-추적--비용-계산)
  - [21. 알림 서비스](#21-알림-서비스)
  - [22. 설정 시스템](#22-설정-시스템)
- [Part VII: 구조적 분석](#part-vii-구조적-분석)
  - [23. 상태 관리 (ChatState)](#23-상태-관리-chatstate)
  - [24. 에러 처리 패턴](#24-에러-처리-패턴)
  - [25. 의존성 그래프 & 커플링](#25-의존성-그래프--커플링)
- [핵심 흐름 다이어그램](#핵심-흐름-다이어그램)
- [구조적 문제 Top 10](#구조적-문제-top-10-영향도-순위)

---

# Part I: 기반 레이어

## 1. 앱 부트스트랩 & DI

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `src/Cominomi/MauiProgram.cs` | 93 | DI 컨테이너 전체 설정 |
| `src/Cominomi/App.xaml.cs` | 22 | MAUI App 수명주기 |
| `src/Cominomi/MainPage.xaml` | 9 | BlazorWebView 호스트 |
| `src/Cominomi/Components/Routes.razor` | - | Blazor 라우팅 |

### 현재 동작
`MauiProgram.cs:14-92`에서 앱이 부트스트랩됨:

1. **Serilog 초기화** (`:16-33`): 콘솔 + 롤링 파일 로그 (14일 보관)
2. **MAUI 설정** (`:35-41`): BlazorWebView + OpenSans 폰트
3. **MudBlazor** (`:50-59`): Snackbar 설정 (하단 우측, 3초, 최대 3개)
4. **서비스 등록** (`:62-85`): **24개 전부 Singleton**

```
등록 순서 (MauiProgram.cs:62-85):
  IShellService         → ShellService
  ChatState             → ChatState (인터페이스 없음)
  IGitService           → GitService
  IGhService            → GhService
  IClaudeService        → ClaudeService
  IContextService       → ContextService
  IMemoryService        → MemoryService
  IHooksEngine          → HooksEngine
  ISkillRegistry        → SkillRegistry
  ITaskService          → TaskService
  ISessionService       → SessionService
  ISessionGitWorkflowService → SessionGitWorkflowService
  IWorkspaceService     → WorkspaceService
  ISettingsService      → SettingsService
  IDependencyCheckService → DependencyCheckService
  ISpotlightService     → SpotlightService
  IFolderPickerService  → FolderPickerService (플랫폼별)
  IFilePickerService    → FilePickerService (플랫폼별)
  IAttachmentService    → AttachmentService
  IPluginService        → PluginService
  IUsageService         → UsageService
  IMcpService           → McpService
  INotificationService  → NotificationService (플랫폼별)
  IActivityService      → ActivityService
```

`MainPage.xaml`은 `<BlazorWebView>`를 호스트하며, `Routes.razor`가 `MainLayout`으로 라우팅.

### 빠진 것 / 문제점
- **모든 서비스가 Singleton**: `ChatState`는 가변 `ConcurrentDictionary`를 가지고, `SkillRegistry`는 가변 `List`를 가짐. 스레드 안전성이 관례에만 의존
- **옵션 패턴 미사용**: 각 서비스가 `ISettingsService.LoadAsync()`를 직접 호출하여 설정을 읽음. `IOptions<T>` 패턴으로 중앙화 가능
- **서비스 건강 체크 없음**: Git/gh/Claude CLI가 없어도 앱이 시작됨 (런타임에만 실패)
- **Graceful shutdown 없음**: 스트리밍 중인 Claude 프로세스가 앱 종료 시 고아 프로세스가 될 수 있음
- **ChatState에 인터페이스 없음** (`:63`): 직접 클래스로 등록되어 테스트 불가

---

## 2. 영속화 레이어 (JSON 파일 스토리지)

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/AppPaths.cs` | 23 | 디렉토리 경로 정의 |
| `Shared/Services/JsonDefaults.cs` | 12 | 공유 직렬화 옵션 |
| `Shared/Services/SettingsService.cs` | 38 | settings.json 캐시 |
| `Shared/Services/UsageService.cs` | 204 | usage.jsonl (별도 위치!) |

### 현재 동작

**디렉토리 구조** (`AppPaths.cs:5-16`):
```
%APPDATA%/Cominomi/
├── settings.json          ← SettingsService (인메모리 캐시)
├── hooks.json             ← HooksEngine
├── sessions/              ← SessionService
│   └── {uuid}.json        ← 세션 + 전체 메시지 포함
├── workspaces/            ← WorkspaceService
│   └── {uuid}.json
├── repos/                 ← WorkspaceService (리포 메타데이터)
│   └── {uuid}.json
├── memory/                ← MemoryService
│   └── {uuid}.json
├── tasks/                 ← TaskService
│   └── {uuid}.json
└── archived-contexts/     ← SessionService (아카이브)
    └── {workspaceName}/{sessionName}/.context/
```

**별도 위치**: `UsageService`는 `Environment.SpecialFolder.LocalApplicationData`에 `usage.jsonl`을 저장 (다른 데이터와 불일치).

**읽기 패턴**: 모든 서비스가 `Directory.GetFiles(dir, "*.json")` → 파일마다 `File.ReadAllTextAsync` → `JsonSerializer.Deserialize` → 실패 시 로그 경고 후 건너뜀.

**쓰기 패턴**: `JsonSerializer.Serialize` → `File.WriteAllTextAsync`로 전체 파일 덮어쓰기. 원자적 쓰기가 아님.

**세션 파일 특이사항** (`SessionService.cs:31-78`):
- `GetSessionsAsync()`: 모든 세션 파일을 읽되, 메시지 없이 15+ 프로퍼티만 수동 복사하여 경량 목록 생성
- `LoadSessionAsync()`: 전체 파일 읽기 (메시지 포함) + `MigrateToParts()` 호출

### 빠진 것 / 문제점
- **파일 락 없음**: 병렬 세션이 동시에 `SaveSessionAsync()`를 호출하면 데이터 손상 가능. 특히 스트리밍 중 `content_block_stop`마다 중간 저장 (`ChatView.razor:747`)
- **세션 파일 무한 성장**: 100턴 대화 + 도구 호출 결과가 하나의 JSON 파일에 전부 포함. 수 MB까지 성장 가능
- **인덱싱 없음**: `GetSessionsAsync()`가 모든 파일을 역직렬화 — O(n) 성능. 세션 수 증가 시 사이드바 로딩 느려짐
- **백업/마이그레이션 없음**: JSON 스키마 변경 시 기존 파일이 역직렬화 실패 → 데이터 손실
- **Usage 위치 불일치**: `AppData/Roaming`과 `AppData/Local`에 분산 저장
- **GetSessionsAsync 수동 매핑** (`SessionService.cs:46-68`): 15개 프로퍼티를 수동 복사. DTO나 프로젝션이 없음

---

## 3. 데이터 모델

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Models/Session.cs` | 56 | 핵심 엔티티 (30+ 프로퍼티) |
| `Shared/Models/Workspace.cs` | 42 | 리포 설정 |
| `Shared/Models/ChatMessage.cs` | 53 | 메시지 + Parts |
| `Shared/Models/StreamEvent.cs` | 165 | Claude CLI 스트림 프로토콜 |
| `Shared/Models/ToolCall.cs` | 11 | 도구 호출 기록 |
| `Shared/Models/AppSettings.cs` | 23 | 앱 설정 |

### Session (핵심 엔티티)

`Session.cs:19-56` — 하나의 클래스에 3가지 관심사가 혼재:

```
[대화 관심사]
  Id, Title, Messages, Model, PermissionMode, EffortLevel, AgentType
  ConversationId, MaxTurns, MaxBudgetUsd
  TotalInputTokens, TotalOutputTokens
  AllowedTools, DisallowedTools, AdditionalDirs

[Git 관심사]
  WorktreePath, BranchName, BaseBranch, WorkspaceId, IsLocalDir

[GitHub/PR 관심사]
  Status, PrUrl, PrNumber, IssueNumber, IssueUrl, ConflictFiles
  PlanCompleted, PlanFilePath
```

### 상태 머신 (SessionStatus)

`Session.cs:7-17` — 8가지 상태, 전이 규칙은 서비스 코드에만 있음:

```
Initializing ──→ Pending ──→ Ready ──→ Pushed ──→ PrOpen ──→ Merged ──→ Archived
                    │           │                     │
                    │           └── Error              └── ConflictDetected
                    │                                          │
                    └── Error                                  └── Ready (수동 해결 후)
```

### ChatMessage 이중 저장

`ChatMessage.cs:17-46`:
```csharp
public string Text { get; set; }           // 레거시: 전체 텍스트
public List<ToolCall> ToolCalls { get; set; } // 레거시: 도구 호출 목록
public List<ContentPart> Parts { get; set; }  // 신규: 인터리브 렌더링
```

`MigrateToParts()` (`:36-45`): 매 로드마다 호출. Parts가 비어있으면 ToolCalls→Parts, Text→Parts로 변환.

### 빠진 것 / 문제점
- **검증 없음**: 빈 `WorkspaceId`로 Session 생성 가능. 음수 토큰 카운트 가능. 아무 상태에서 아무 상태로 전환 가능
- **Session이 너무 큼**: 대화 + Git + PR을 하나에 담아서, 하나를 변경하면 전체를 다시 직렬화
- **Parts 이중 저장**: `Text`와 `Parts[].Text`가 동시에 존재. `AppendText()`가 양쪽 다 업데이트 (`ChatState.cs:184-202`)
- **모든 모델이 가변**: `public set`만 있고, 불변(immutable) 보장 없음. 어디서든 조용히 수정 가능
- **ModelDefinitions 하드코딩**: opus/sonnet/haiku 3개 모델만. 새 모델 추가는 코드 변경 필요
- **UI 전용 모델이 도메인과 혼재**: `ContentGroup`, `ActivitySummaryInfo` 등이 `Models/` 네임스페이스에 있음

---

# Part II: 외부 도구 통합

## 4. 셸 서비스 & 프로세스 실행

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/ShellService.cs` | 202 | 셸 감지, WhichAsync |
| `Shared/Services/IShellService.cs` | 21 | 인터페이스 |

### 현재 동작

`ShellService.cs:119-131` — 셸 감지 (1회 캐싱):
```
Windows: Git Bash 탐색 (where git → ../bin/bash.exe, 또는 잘 알려진 경로)
         실패 시 → cmd.exe 폴백
macOS/Linux: /bin/sh
```

`WhichAsync()` (`ShellService.cs:36-113`):
- Windows cmd: `where.exe {name}`
- Git Bash: `cygpath -w "$(which {name})"` (Unix→Windows 경로 변환)
- Sh: `which {name}`
- 3초 타임아웃

### 프로세스 실행 패턴 — 4곳에 복사-붙여넣기

```
GitService.cs:423-443     — CreateGitProcess(): Process 직접 생성, FileName="git"
GhService.cs:128-159      — RunGhAsync(): Process 직접 생성, FileName="gh"
ClaudeService.cs:173-201  — StartProcess(): Process 직접 생성, FileName=resolved
HooksEngine.cs:66-103     — FireAsync(): ShellService 통해 셸 래핑
```

4가지 서비스가 각각 `Process`를 직접 생성하며, 타임아웃/에러처리/환경변수 설정이 전부 다름.

### 빠진 것 / 문제점
- **통합 프로세스 실행 추상화 없음**: `RunProcessAsync(fileName, args, workDir, timeout, envVars)` 같은 공통 메서드가 없어서, 동일한 보일러플레이트가 4곳에 복붙
- **셸 감지 1회 캐싱**: 앱 실행 후 Git 설치하면 재시작 필요
- **WhichAsync 3초 고정 타임아웃**: 에러 전파 없음, 실패 시 null 반환
- **GitService/GhService는 ShellService를 사용하지 않음**: `"git"`, `"gh"`를 직접 하드코딩

---

## 5. Git 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/GitService.cs` | 444 | 모든 git 명령 |
| `Shared/Services/IGitService.cs` | 33 | 인터페이스 |

### 현재 동작

**핵심 메서드**:
- `CloneAsync()` (`:17-85`): progress 리포트 포함. CancellationToken 지원
- `AddWorktreeAsync()` (`:87-100`): 브랜치 존재 여부 확인 후 분기
- `RemoveWorktreeAsync()` (`:102-117`): `--force` + 디렉토리 수동 삭제 + prune
- `DetectDefaultBranchAsync()` (`:119-139`): symbolic-ref → main 확인 → master 확인 → 현재 브랜치
- `PushBranchAsync()` (`:215-218`): `git push -u origin`
- `PushForceBranchAsync()` (`:220-223`): `git push --force-with-lease origin`
- `ParseDiff()` (`:348-421`): 정적 메서드, `--name-status` + unified diff → `DiffSummary` 구조화

**프로세스 실행** (`RunGitAsync()`, `:183-197`):
```csharp
var stdout = await process.StandardOutput.ReadToEndAsync(ct);
var stderr = await process.StandardError.ReadToEndAsync(ct);
await process.WaitForExitAsync(ct);
```
stdout/stderr 전체를 메모리로 읽은 후 반환.

### 데이터 흐름
```
SessionService ──→ GitService.AddWorktreeAsync() (세션 생성)
SessionService ──→ GitService.RemoveWorktreeAsync() (세션 정리)
SessionGitWorkflowService ──→ GitService.PushBranchAsync() (브랜치 푸시)
SidebarExplorer ──→ GitService.ListTrackedFilesAsync() (파일 목록)
SidebarChanges ──→ GitService.GetNameStatusAsync() + ParseDiff() (변경사항)
ChatView ──→ GitService.RenameBranchAsync() (제목 기반 브랜치 이름 변경)
```

### 빠진 것 / 문제점
- **타임아웃 없음**: `CloneAsync` 외에는 CancellationToken이 `default`로 전달. 대형 리포의 `git push`가 무한 대기 가능
- **stdout 전체 메모리 로드** (`:189`): 대형 diff 출력 시 메모리 문제
- **ParseDiff 취약** (`:395`): `header.LastIndexOf(" b/")`로 파일 경로 추출 — 경로에 공백이나 ` b/`가 포함되면 실패
- **캐싱 없음**: `DetectDefaultBranchAsync`, `ListBranchesAsync`가 매번 git 프로세스 생성
- **`git` 하드코딩** (`:429`): PATH에 git이 없으면 실패. 설정으로 경로 지정 불가
- **git clone → push 사이에 fetch 없음**: 다른 사람이 base branch에 푸시한 변경사항 반영 안 됨

---

## 6. GitHub CLI 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/GhService.cs` | 161 | PR/이슈 CRUD |
| `Shared/Services/IGhService.cs` | 15 | 인터페이스 |

### 현재 동작

```
CreatePrAsync()  (GhService.cs:16-23)  → gh pr create --base --head --title --body
MergePrAsync()   (GhService.cs:25-30)  → gh pr merge {number} --{method}
GetPrForBranchAsync() (GhService.cs:32-55) → gh pr view {branch} --json number,url,state
IsAuthenticatedAsync() (GhService.cs:57-61) → gh auth status (workingDir=".")
ListIssuesAsync() (GhService.cs:72-100)    → gh issue list --json --limit 30
```

### 빠진 것 / 문제점
- **커맨드 인젝션 위험** (`:18-19`): title/body 이스케이핑이 `Replace("\"", "\\\"")`만. 셸 메타문자 (`$`, `` ` ``, `$(...)`) 미처리
  ```csharp
  var escapedTitle = title.Replace("\"", "\\\"");
  // title = "test $(rm -rf /)" → 셸에서 실행됨
  ```
- **페이지네이션 없음** (`:75`): `--limit 30` 하드코딩. 31번째 이슈부터 안 보임
- **PR 병합 시 체크 대기 없음**: CI가 돌고 있어도 즉시 병합 시도
- **IsAuthenticatedAsync의 workingDir가 "."** (`:59`): 현재 디렉토리가 git 리포가 아닐 수 있음
- **`gh` 하드코딩** (`:135`): PATH에 gh가 없으면 실패. 설정으로 경로 지정 불가
- **rate limiting 인식 없음**: GitHub API 제한에 도달해도 재시도 없음

---

## 7. Claude CLI 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/ClaudeService.cs` | 373 | CLI 프로세스 관리 + 스트리밍 |
| `Shared/Services/IClaudeService.cs` | 33 | 인터페이스 |
| `Shared/Services/ClaudeArgumentBuilder.cs` | 111 | CLI 인자 구성 |
| `Shared/Services/ClaudeCliResolver.cs` | 116 | npx/직접실행 감지 |
| `Shared/Models/StreamEvent.cs` | 165 | 스트림 이벤트 모델 |

### 현재 동작

**SendMessageAsync** (`ClaudeService.cs:29-171`) — `IAsyncEnumerable<StreamEvent>`:

```
1. 설정 로드 + CLI 경로 해석 + 능력 감지 (버전, --verbose 지원)
2. 기존 세션의 프로세스 있으면 킬
3. ClaudeArgumentBuilder.Build()로 인자 구성
4. Process 시작, stdin에 메시지 쓰기 + stdin 닫기
5. stdout에서 한 줄씩 읽기 → JSON 역직렬화 → StreamEvent yield
6. 프로세스 종료 대기 (30초 타임아웃)
7. --verbose 필요하면 재시도 (동일 로직 60줄 복붙)
8. stderr에 에러 있으면 합성 error 이벤트 yield
```

**인자 구성** (`ClaudeArgumentBuilder.cs:8-110`):
```
기본: --print --output-format stream-json [--verbose] [--debug]
모델: --model {model}
권한: --dangerously-skip-permissions | --permission-mode {mode}
노력: --effort {level}
대화 이어가기: --resume {conversationId} [--fork-session]
제한: --max-turns {n} --max-budget-usd {n}
폴백: --fallback-model {model}
MCP: --mcp-config "{path}"
추가 디렉토리: --add-dir "{dir}" (복수)
도구 제한: --allowedTools "{tool}" / --disallowedTools "{tool}"
시스템 프롬프트: --append-system-prompt "{escaped}" ← 이스케이핑 취약
```

**SummarizeAsync** (`ClaudeService.cs:293-349`): 별도 프로세스 (Haiku 모델, 15초 타임아웃, `--print --output-format text`)

### 데이터 흐름
```
ChatView.ProcessMessageAsync()
  │
  ├─ ClaudeService.SendMessageAsync() ←── IAsyncEnumerable<StreamEvent>
  │     │
  │     ├─ ClaudeCliResolver.ResolveAsync() → (fileName, baseArgs)
  │     ├─ DetectCapabilitiesAsync() → CliCapabilities
  │     ├─ ClaudeArgumentBuilder.Build() → arguments string
  │     ├─ StartProcess() → Process
  │     └─ readline loop → JsonSerializer.Deserialize<StreamEvent>
  │
  └─ ClaudeService.SummarizeAsync() ←── string? (제목)
```

### 빠진 것 / 문제점
- **프로세스 재사용 없음**: 매 메시지마다 새 프로세스 생성 + 종료. `--resume`으로 대화 맥락은 유지하지만 프로세스 오버헤드 있음
- **크래시 복구 없음**: 스트리밍 중 프로세스 사망 시 부분 상태만 저장. 사용자는 대화가 끊긴 것만 인지
- **시스템 프롬프트 이스케이핑 취약** (`ClaudeArgumentBuilder.cs:105`):
  ```csharp
  var escaped = systemPrompt.Replace("\\", "\\\\").Replace("\"", "\\\"");
  ```
  줄바꿈(`\n`), 작은따옴표, 유니코드 등 미처리. 시스템 프롬프트에 이들이 포함되면 CLI 인자가 깨짐
- **재시도 로직 60줄 복붙** (`:106-148`): `--verbose` 재시도가 스트리밍 루프 전체를 복사
- **AgentProcess.Cancel()에서 Kill(entireProcessTree: true)** (`:367`): 일부 플랫폼에서 고아 프로세스 남을 수 있음
- **stderr 수집 태스크 실패 무시** (`:102`): `try { await stderrTask; } catch { }` — 에러 정보 손실

---

# Part III: 워크플로우 오케스트레이션

## 8. 세션 생명주기

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/SessionService.cs` | 401 | 세션 CRUD + 워크트리 |
| `Shared/Services/ISessionService.cs` | 18 | 인터페이스 |

### 현재 동작

**3가지 생성 경로**:

| 경로 | 메서드 | 워크트리 | 브랜치 | 상태 |
|------|--------|----------|--------|------|
| Pending | `CreatePendingSessionAsync()` (`:131-161`) | 없음 (나중에) | 없음 | Pending |
| Full | `CreateSessionAsync()` (`:86-129`) | 즉시 생성 | 즉시 생성 | Ready/Error |
| LocalDir | `CreateLocalDirSessionAsync()` (`:163-195`) | 리포 루트 사용 | 없음 | Ready |

**Pending→Ready 전환** (`InitializeWorktreeAsync()`, `:197-242`):
```
1. 세션 로드
2. 워크스페이스 로드
3. 브랜치 이름 생성: cominomi/{yyyyMMdd-HHmmss}
4. git worktree add -b {branch} {path} {baseBranch}
5. .context/ 디렉토리 초기화
6. 상태 → Ready
```

**정리** (`CleanupSessionAsync()`, `:301-356`):
```
1. .context/ 아카이브
2. (LocalDir 아닌 경우) 워크트리 제거
3. (LocalDir 아닌 경우) 브랜치 삭제
4. 상태 → Archived
5. OnSessionArchive 훅 발사
```

**브랜치 이름 생성** (`GenerateBranchName()`, `:375-391`):
```
입력 → 소문자 → 공백→하이픈 → 비알파벳숫자 제거 → 40자 제한
→ cominomi/{slug 또는 타임스탬프}
```

### 빠진 것 / 문제점
- **한국어 제목 → 빈 슬러그**: `[^a-z0-9\-]` 정규식 (`:396`)이 한글을 전부 제거. 결과: `cominomi/20260318-143022` (타임스탬프 폴백)
- **세션 수 제한 없음**: 워크스페이스당 수백 개 워크트리 생성 가능. git 성능 저하
- **상태 전이 검증 없음**: Archived에서 Ready로 직접 변경해도 에러 없음
- **GetSessionsAsync O(n)** (`:31-78`): 파일 수 만큼 직렬화. 페이지네이션 없음
- **워크트리 초기화 레이스 컨디션**: Pending 세션에 메시지 전송 시 `InitializeWorktreeAsync`가 호출되는데, 빠르게 두 번 전송하면 워크트리 이중 생성 시도 가능
- **CityName 아카이브 경로**: `CityNames.GetRandom()` (46개 도시)으로 이름 생성, 하지만 파일 경로에 부적합한 문자 없음을 보장하지 않음

---

## 9. Git 워크플로우 파이프라인

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/SessionGitWorkflowService.cs` | 250 | Push→PR→Merge 파이프라인 |
| `Shared/Services/ISessionGitWorkflowService.cs` | 13 | 인터페이스 |

### 현재 동작

**MergeAllAsync** (`SessionGitWorkflowService.cs:177-229`) — 원클릭 파이프라인:
```
Ready ──→ PushBranchAsync() ──→ Pushed
Pushed ──→ CreatePrAsync() ──→ PrOpen
PrOpen ──→ MergePrAsync() ──→ Merged (또는 ConflictDetected)
```

각 단계는 실패 시 중단하고 `session.ErrorMessage` 설정.

**충돌 감지** (`MergePrAsync()`, `:160-163`):
```csharp
var errorLower = (result.Error + result.Output).ToLowerInvariant();
if (errorLower.Contains("conflict") || errorLower.Contains("merge") || errorLower.Contains("not mergeable"))
```
문자열 기반 감지.

**CheckMergeStatusAsync** (`:31-55`):
```
git merge-base --is-ancestor {branchName} {baseBranch}
→ 성공이면 이미 병합됨 → 상태를 Merged로 변경
```

### 데이터 흐름
```
ChatView UI 버튼
  │
  ├─ "PR 생성" → ChatView.CreatePr() → AI 프롬프트 기반 PR 생성
  │                 (또는 SessionGitWorkflowService.CreatePrAsync() 직접 호출)
  │
  ├─ "병합" → SessionGitWorkflowService.MergePrAsync()
  │
  └─ "강제 푸시" → SessionGitWorkflowService.PushBranchAsync(force: true)
```

### 빠진 것 / 문제점
- **PR 생성 경로 2개**: ChatView의 `CreatePr()`는 AI 프롬프트 기반 (`:59`에서 ClaudeService 호출), `SessionGitWorkflowService.CreatePrAsync()`는 직접 `gh pr create` 호출. 결과가 다를 수 있음
- **자동 리베이스 없음**: base branch가 진행된 경우, 병합 실패. 사용자에게 수동 리베이스를 안내하지 않음
- **같은 세션 3~4번 로드**: `MergeAllAsync`가 `PushBranchAsync`를 호출 → 내부에서 `LoadSessionAndWorkspaceAsync` → 또 `LoadSessionAsync`. 파이프라인 한 번에 세션 파일을 3~4회 읽음
- **롤백 없음**: PR 생성 성공 후 병합 실패 시, PR이 열린 채로 방치
- **강제 푸시에 확인 없음**: `ForcePushAndMerge`가 UI에서 직접 호출 (`ChatView.razor:99`)
- **충돌 감지 오탐 가능**: `"merge"` 단어가 에러와 무관한 맥락에 나올 수 있음

---

## 10. ChatView — 중앙 오케스트레이터 (핵심 문제)

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Components/Chat/ChatView.razor` | ~1,461 | **God Component** |

### 현재 동작

**주입 서비스 18개** (`:1-21`):
```
ChatState, ClaudeService, SessionService, GitWorkflow, GitService,
GhService, WorkspaceService, AttachmentService, ContextService,
MemoryService, HooksEngine, JSRuntime, Logger, Snackbar,
UsageService, NotificationService, SettingsService
```

**이 컴포넌트가 담당하는 것들**:

| 기능 | 줄 범위 (대략) | 설명 |
|------|----------------|------|
| 랜딩 페이지 | `:111-147` | 세션 없을 때 워크스페이스 생성 + 최근 대화 |
| 브랜치 선택 | `:155-220` | Pending 세션의 브랜치 피커 (검색/필터) |
| 워크플로우 바 | `:23-107` | PR 생성/병합/충돌 해결/강제 푸시 버튼 |
| 메시지 렌더링 | `:110-400` | MessageBubble 루프 + 스트리밍 인디케이터 |
| 입력 영역 | `:400+` | InputArea 래핑 + 이벤트 핸들링 |
| 메시지 전송 | `HandleSend` | 워크트리 초기화 + 첨부파일 + 스킬 확장 |
| 스트림 처리 | `ProcessMessageAsync` | 300줄 switch문 (12개 이벤트 타입) |
| 사용량 추적 | switch 내부 | 4단계 폴백 (result→accumulated→cost-only→post-loop) |
| 플랜 모드 | finally 블록 | 3계층 감지 (tool_use→텍스트 검색→파일 감지) |
| 질문 감지 | finally 블록 | QuestionDetector → QuickResponseBar |
| PR 워크플로우 | 별도 메서드 | CreatePr, MergePr, ForcePush, ResolveConflicts |
| 알림 | 별도 메서드 | 윈도우 포커스 감지 + 조건부 토스트 |

### ProcessMessageAsync 스트림 처리 switch문

```
case "system" when subtype == "init"    → session.ConversationId 저장
case "content_block_start" (thinking)   → SetPhase(Thinking)
case "content_block_start" (tool_use)   → AddToolCall + SetPhase(UsingTool)
case "content_block_start" (tool_result)→ toolResultBlockMap에 등록
case "content_block_delta"              → 텍스트/사고/JSON 델타/도구결과 처리
case "content_block_stop"               → 도구 완료 + 중간 저장
case "assistant"                        → 완성된 메시지 블록 처리
case "user"                             → tool_result 매칭
case "message_start"                    → 모델 + 입력 토큰 캡처
case "message_delta"                    → 토큰 누적 + max_tokens 감지
case "message_stop"                     → (아무것도 안 함)
case "result"                           → ConversationId + 사용량 기록 + 폴백 텍스트
case "error"                            → 에러 메시지 추가
```

### 빠진 것 / 문제점
- **God Component**: 1,461줄에 18개 서비스. 어떤 기능을 변경해도 이 파일에 영향
- **스트림 처리 300줄 switch**: `StreamEventProcessor` 같은 별도 서비스로 분리 가능하지만 안 되어 있음
- **사용량 추적 4단계 폴백**: Claude CLI의 사용량 보고가 불일치하여 4가지 경로로 처리. 복잡도 높음
- **플랜 모드 3계층 감지**: `ExitPlanMode` 도구 호출이 불안정하여 텍스트 검색 + 파일 감지까지 폴백. 근본 원인 미해결
- **ProcessMessageAsync가 Task.Run에서 실행**: 백그라운드 스레드에서 `InvokeAsync(StateHasChanged)` 호출. 동작하지만 예외 미관찰 위험
- **async void HandleStateChanged**: 예외 미관찰 (`async void` 패턴)
- **CreatePr가 AI 기반**: 사용자가 "PR 생성" 클릭 → AI에게 프롬프트 → AI가 PR 제목/본문 생성 → 실제 생성. 하지만 직접 `gh pr create`하는 `SessionGitWorkflowService` 경로도 존재

---

# Part IV: UI 컴포넌트 시스템

## 11. 레이아웃 아키텍처

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Components/Layout/MainLayout.razor` | 238 | 3패널 레이아웃 |
| `Shared/Components/Layout/DetailPanel.razor` | ~45 | 우측 패널 |
| `Shared/Components/Layout/MainTabBar.razor` | ~29 | 탭 바 |
| `Shared/Components/Layout/StatusBar.razor` | ~21 | 상태 바 |
| `Shared/Services/TabManager.cs` | 155 | 탭 관리 |

### 현재 동작

`MainLayout.razor` — 3패널 구조:
```
┌─────────────────────────────────────────────────────────────┐
│ aside.cominomi-sidebar  │ main.cominomi-main │ aside.cominomi-detail │
│                         │                    │                       │
│  ┌─ sidebar-toolbar ─┐  │  ┌─ main-toolbar ─┐│  ┌─ DetailPanel ─┐   │
│  │ 로고 + 설정 버튼  │  │  │ 제목+브랜치+탐색││  │ SidebarExplorer│   │
│  └───────────────────┘  │  └────────────────┘│  │ 또는           │   │
│  ┌─ SessionList ─────┐  │  ┌─ MainTabBar ───┐│  │ SidebarChanges │   │
│  │ 워크스페이스/세션  │  │  │ (탭 2개 이상시) ││  └───────────────┘   │
│  │                    │  │  └────────────────┘│                       │
│  └───────────────────┘  │  ┌─ main-content ──┐│                       │
│                         │  │ ChatView 또는    ││                       │
│                         │  │ ActivityView 또는││                       │
│                         │  │ FileContentView ││                       │
│                         │  └────────────────┘│                       │
└─────────────────────────────────────────────────────────────┘
```

**MainLayout 추가 책임** (`:142-237`):
- 테마 관리 (MudTheme + CSS 변수 + `setTheme` JSInterop)
- 의존성 체크 (`DependencyCheckService`) → SetupDialog 표시
- UsageDashboard 다이얼로그
- 사이드바 토글

**TabManager** (`TabManager.cs`):
- 타입: Chat, FileContent, FileDiff, Activity
- Chat 탭은 항상 존재. 다른 탭은 동적 추가/제거
- `OnTabChanged` 이벤트

### 빠진 것 / 문제점
- **MainLayout이 너무 많은 책임**: 레이아웃 + 테마 + 의존성 체크 + 다이얼로그 관리
- **패널 크기 조절 불가**: CSS 고정 너비. 사용자가 사이드바나 디테일 패널 크기를 조절할 수 없음
- **키보드 네비게이션 없음**: 패널 간 이동에 접근성 지원 없음
- **탭 메모리에 파일 콘텐츠 보관**: `MainTab.FileContent`가 문자열로 메모리에 상주. 퇴출 정책 없음

---

## 12. 사이드바 & 세션 관리 UI

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Components/Sidebar/SessionList.razor` | ~719 | **두 번째 God Component** |
| `Shared/Components/Sidebar/CreateWorkspaceDialog.razor` | ~282 | 워크스페이스 생성 |
| `Shared/Components/Sidebar/SidebarExplorer.razor` | ~396 | 파일 트리 |
| `Shared/Components/Sidebar/SidebarChanges.razor` | ~212 | 변경사항 뷰 |

### 현재 동작

**SessionList** — 워크스페이스 + 세션 계층 표시:
- 워크스페이스 그룹화
- 세션별 상태 인디케이터 (색상 dot)
- 세션 생성 (Pending/LocalDir)
- 세션 삭제/아카이브
- Merge All (전체 병합)
- 워크스페이스 설정 바로가기
- DiffStat 캐싱 (세션별)
- 키보드 단축키 (Ctrl+1/Cmd+1, JSInterop)
- 스트리밍 상태 표시 (세션별 독립)

**SidebarExplorer** — 파일 트리:
- `FileSystemWatcher`로 변경 감지 (디바운스)
- `.git`, `.context` 디렉토리 필터링
- 파일 클릭 → 탭으로 열기
- 세션별 확장 상태 유지

### 빠진 것 / 문제점
- **SessionList 719줄 God Component**: 워크스페이스 CRUD + 세션 목록 + 세션 액션 + 컨텍스트 메뉴 + 설정 단축키 + 키보드 핸들링
- **가상화 없음**: 세션이 많아지면 전체 DOM에 렌더링
- **세션 선택 시 전체 로드**: `LoadSessionAsync()`가 전체 메시지 포함 파일을 동기적으로 읽음. 대형 세션은 UI 버벅임
- **FileSystemWatcher 플러딩**: Windows에서 빠른 파일 변경 시 이벤트 폭주 → UI 업데이트 과다

---

## 13. 채팅 컴포넌트

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Components/Chat/MessageBubble.razor` | ~286 | 메시지 렌더링 |
| `Shared/Components/Chat/InputArea.razor` | ~459 | 입력 영역 |
| `Shared/Components/Chat/StreamingIndicator.razor` | ~122 | 스트리밍 상태 |
| `Shared/Components/Chat/PlanReviewBar.razor` | ~128 | 플랜 리뷰 |
| `Shared/Components/Chat/QuickResponseBar.razor` | ~19 | 빠른 응답 |
| `Shared/Components/Tools/ToolCallCard.razor` | ~130 | 도구 호출 카드 |
| `Shared/Services/ContentGrouper.cs` | ~244 | 파트 그룹화 |
| `Shared/Services/QuestionDetector.cs` | ~96 | 질문 감지 |

### 현재 동작

**MessageBubble**: ContentGrouper로 Parts를 그룹화:
- ToolGroup: 연속된 도구 호출을 접기
- Thinking: 사고 블록 접기
- Text: 최종 텍스트와 중간 텍스트 구분

**InputArea** (459줄 — 또 다른 복잡한 컴포넌트):
- 텍스트 입력 (자동 크기 조절)
- Enter 전송, Shift+Enter 줄바꿈
- 파일 첨부 (드래그/붙여넣기)
- 모델 선택 드롭다운
- 권한 모드 선택
- 노력 수준 토글
- 에이전트 타입 선택

**도구 위젯**: 도구 타입별 전문 렌더링
- `BashToolWidget`: 명령어 + 출력
- `EditToolWidget`: 파일 경로 + 변경 내용
- `GlobToolWidget`: 패턴 + 매칭 파일
- `GrepToolWidget`: 검색 패턴 + 결과
- `ReadToolWidget`: 파일 경로 + 내용

### 빠진 것 / 문제점
- **"중간 텍스트" 휴리스틱** (`ContentGrouper.cs`): 하드코딩된 한국어/영어 패턴으로 텍스트를 "중간"으로 분류. 오분류 가능
- **InputArea 459줄**: 텍스트 입력 + 파일 첨부 + 모델/권한/노력 선택이 한 컴포넌트에
- **도구 입력 JSON 매 렌더 파싱**: ToolCallCard가 `JsonSerializer.Deserialize`를 매 렌더마다 수행. 캐싱 없음
- **QuestionDetector 제한적**: 마지막 문장이 `?`로 끝나야 질문으로 감지. 질문이 본문 중간에 있으면 놓침

---

# Part V: 확장성 시스템

## 14. 훅 엔진

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/HooksEngine.cs` | 126 | 이벤트 기반 셸 실행 |
| `Shared/Models/HookDefinition.cs` | ~38 | 훅 정의 모델 |

### 현재 동작

`HooksEngine.cs:57-111` — `FireAsync()`:
```
이벤트 발생 → _hooks에서 해당 이벤트 + Enabled 필터
→ 각 훅에 대해:
  1. ShellService.GetShellAsync()
  2. Process 시작 (셸 래핑: bash -c "command" 또는 cmd /c "command")
  3. 환경 변수 주입 (COMINOMI_SESSION_ID, COMINOMI_HOOK_EVENT 등)
  4. 5초 타임아웃 대기
  5. 실패 시 로그 경고
```

**지원 이벤트**: OnSessionCreate, OnSessionArchive, OnBranchPush, OnMessageComplete

### 빠진 것 / 문제점
- **5초 타임아웃 하드코딩** (`:95`): 훅별 타임아웃 설정 불가
- **직렬 실행** (`:63`): `foreach`로 순차 실행. 느린 훅이 뒤의 훅을 차단
- **출력 미캡처**: stdout/stderr를 redirect하지만 읽지 않음
- **커맨드 이스케이핑 최소** (`:68`): `Replace("\"", "\\\"")`만

---

## 15. 스킬 레지스트리 (슬래시 커맨드)

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/SkillRegistry.cs` | ~191 | 빌트인 + 커스텀 스킬 |
| `Shared/Services/SkillFileStore.cs` | ~179 | 파일 기반 커스텀 스킬 |

### 현재 동작

**빌트인 스킬 11개** (하드코딩):
```
/commit, /review, /simplify, /test, /explain, /fix,
/plan, /compact, /security-review, /pr-comments, /debug
```

**커스텀 스킬 로드**:
- `~/.claude/commands/*.md` (사용자 범위)
- `<project>/.claude/commands/*.md` (프로젝트 범위)

**확장**: `{args}` 또는 `$ARGUMENTS` 플레이스홀더 → 호출 시 실제 인자로 치환

### 빠진 것 / 문제점
- **빌트인 프롬프트 하드코딩**: 스킬 내용 변경이 코드 변경 필요
- **`{args}` vs `$ARGUMENTS` 이중 표준**: 어느 것을 쓸지 문서화 없음
- **스킬 체이닝 불가**: `/commit` 후 `/review` 자동 실행 같은 워크플로우 불가
- **프로젝트 경로 의존**: 워크스페이스 변경 시에만 커스텀 스킬 리로드

---

## 16. MCP 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/McpService.cs` | ~203 | MCP 서버 관리 |
| `Shared/Models/McpServer.cs` | ~21 | 서버 모델 |
| `Shared/Components/Settings/McpManagerDialog.razor` | ~342 | 관리 UI |

### 현재 동작
`claude mcp list`, `claude mcp add`, `claude mcp remove` CLI 명령 래핑.
CLI의 텍스트 테이블 출력을 regex로 파싱하여 MCP 서버 목록 획득.

### 빠진 것 / 문제점
- **텍스트 테이블 regex 파싱**: CLI 출력 형식 변경 시 즉시 깨짐
- **기존 서버 수정 불가**: 삭제 후 재추가만 가능
- **Windows cmd 셸 호환 문제**: `ImportFromJsonAsync`가 작은따옴표 사용

---

## 17. 컨텍스트 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/ContextService.cs` | ~171 | .context/ 디렉토리 관리 |
| `Shared/Models/ContextInfo.cs` | ~15 | 컨텍스트 DTO |

### 현재 동작
```
워크트리/.context/
├── notes.md       ← 세션 노트
├── todos.md       ← TODO 목록
├── plans/         ← 플랜 파일들
└── attachments/   ← 첨부 파일들
```
- `EnsureContextDirectoryAsync()`: 디렉토리 생성 + `.gitignore`에 `.context/` 추가
- `LoadContextAsync()`: 모든 파일 읽어서 `ContextInfo` 반환
- `BuildContextPrompt()`: 시스템 프롬프트에 주입할 텍스트 생성
- `ArchiveContextAsync()`: 아카이브 경로로 복사

### 빠진 것 / 문제점
- **.gitignore 중복 추가**: `EnsureContextDirectoryAsync` 호출마다 `.context/` 행 추가 가능
- **시스템 프롬프트 크기 제한 없음**: notes.md가 10만 줄이면 그대로 시스템 프롬프트에 들어감
- **아카이브 경로 충돌**: `workspace.Name`/`session.CityName` 조합이 중복되면 덮어쓰기

---

## 18. 메모리 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/MemoryService.cs` | ~85 | 영구 메모리 |
| `Shared/Models/MemoryEntry.cs` | ~23 | 메모리 모델 |

### 현재 동작
`%APPDATA%/Cominomi/memory/` 하위에 JSON 파일로 저장. 모든 메모리를 로드하여 `BuildMemoryPrompt()`로 시스템 프롬프트에 주입.

### 빠진 것 / 문제점
- **모든 메모리가 모든 세션에 주입**: 워크스페이스/프로젝트별 필터링 없음. 관련 없는 메모리도 토큰 소비
- **메모리 검색 없음**: 전체 로드만 가능
- **디렉토리 전체 탐색**: 매번 모든 JSON 파일 읽기

---

# Part VI: 지원 시스템

## 19. 스포트라이트 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/SpotlightService.cs` | ~208 | 워크트리→리포 실시간 동기화 |

### 현재 동작
```
시작:
1. 메인 리포에서 uncommitted 변경 stash
2. 세션 브랜치로 checkout
3. 워크트리 파일을 메인 리포로 복사
4. FileSystemWatcher로 워크트리 변경 감시 (500ms 디바운스)
5. 변경 시 자동 동기화

종료:
1. 변경 폐기 (git checkout .)
2. 원래 브랜치로 복귀
3. stash pop
```

### 빠진 것 / 문제점
- **앱 크래시 시 리포 상태 미복구**: 세션 브랜치에 남아있고, stash가 적용 안 됨
- **동시 Spotlight 제한 없음**: 여러 세션이 동시에 활성화하면 리포 충돌
- **stash 이름 충돌**: `"cominomi-spotlight-backup"` 고정. 중복 stash/pop 시 문제
- **메인 리포 직접 수정**: IDE나 다른 도구와 충돌 가능

---

## 20. 사용량 추적 & 비용 계산

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/UsageService.cs` | ~204 | JSONL + 비용 계산 |
| `Shared/Models/UsageEntry.cs` | ~77 | 사용량 모델 |
| `Shared/Components/Settings/UsageDashboard.razor` | ~248 | 대시보드 |

### 현재 동작
- JSONL 형식으로 append-only 저장
- SHA-256 기반 중복 제거 (`_recordedHashes` 인메모리 HashSet)
- 하드코딩 가격: Opus $15/$75, Sonnet $3/$15, Haiku $0.80/$4.00 (1M 토큰당)
- 캐시 할인: write 1.25x, read 0.1x

### 빠진 것 / 문제점
- **가격 하드코딩**: Anthropic 가격 변경 시 코드 수정 필요
- **중복 제거 HashSet 앱 재시작 시 리셋**: 재시작 후 같은 세션의 사용량이 이중 기록 가능
- **JSONL 무한 성장**: 정리/로테이션 없음
- **대시보드 내보내기 없음**

---

## 21. 알림 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `src/Cominomi/Services/NotificationService.cs` | ~119 | Windows 토스트 |

### 현재 동작
Windows AppNotification API로 토스트 알림. 윈도우 포커스 없을 때 또는 다른 세션 보고 있을 때만 발송.

### 빠진 것 / 문제점
- **macOS 구현 없음**: 프로젝트가 MacCatalyst를 타겟하지만, 알림 서비스는 Windows 전용
- **알림 히스토리 없음**: 놓친 알림 확인 불가

---

## 22. 설정 시스템

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/SettingsService.cs` | ~38 | JSON 캐시 |
| `Shared/Models/AppSettings.cs` | ~23 | 설정 모델 |
| `Shared/Components/Settings/AppSettingsContent.razor` | ~377 | 설정 UI |
| `Shared/Components/Settings/WorkspaceSettingsContent.razor` | ~282 | 워크스페이스 설정 |

### 현재 동작
`settings.json` 1회 로드 → 인메모리 캐시. `SaveAsync()` 시 파일 + `OnSettingsChanged` 이벤트 발사.

### 빠진 것 / 문제점
- **외부 수정 감지 없음**: 다른 프로세스가 settings.json을 수정해도 캐시 무효화 안 됨
- **검증 없음**: 잘못된 경로, 음수 값 허용
- **워크스페이스 Preferences가 자유 텍스트**: 구조화된 설정이 아닌 자유 형식 문자열

---

# Part VII: 구조적 분석

## 23. 상태 관리 (ChatState)

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/ChatState.cs` | 366 | 중앙 상태 허브 |

### 현재 동작

**공개 상태 (30+ 멤버)**:
```
[워크스페이스/세션]  CurrentWorkspace, CurrentSession
[스트리밍]           IsStreaming, Phase, ActiveToolName (세션별)
[메시지 빌더]        AddUserMessage, StartAssistantMessage, AppendText,
                     AppendThinking, AddToolCall, FinishMessage
[UI 패널]            RightPanel, IsSpotlightActive
[설정 페이지]        ShowSettings, SettingsSection, SettingsWorkspaceId
[탭]                 Tabs (TabManager)
[기타]               PendingMessage, OnChange, OnRequestCreateWorkspace
```

**디바운스** (`NotifyStateChanged()`, `:333-358`):
```
스트리밍 중 → 50ms 디바운스 (Timer로 주기적 발사)
스트리밍 아닐 때 → 즉시 발사 + Timer 정리
```

**세션별 스트리밍 상태** (`:23-24`):
```csharp
ConcurrentDictionary<string, SessionStreamingState> _streamingStates
ConcurrentDictionary<string, Session> _activeSessions
```

### 빠진 것 / 문제점
- **God Object**: 워크스페이스 선택, 세션 관리, 메시지 빌딩, 스트리밍 조율, UI 패널 상태, 설정 네비게이션 — 5가지 관심사가 하나에
- **Session 가변 참조 공유**: `AppendText()`가 `ChatMessage.Text`와 `ChatMessage.Parts`를 직접 수정. ChatState와 Session이 같은 객체를 참조
- **디바운스 Timer 생성/소멸 반복**: 스트리밍 시작/종료 시 Timer를 만들었다 지웠다 함. 전환 시점에 알림 누락 가능
- **ConsumePendingMessage 1회성**: 두 번 호출하면 두 번째는 null. 메시지 손실 가능
- **인터페이스 없음**: 직접 클래스로 DI 등록. 테스트 시 모킹 불가
- **Undo/Redo 없음**: 상태 변경 이력 없음

---

## 24. 에러 처리 패턴

### 현재 패턴

| 위치 | 패턴 | 예시 |
|------|------|------|
| 서비스 (파일 I/O) | try-catch → 로그 경고 → 건너뜀 | `SessionService.cs:72` |
| ClaudeService | 합성 error StreamEvent | `ClaudeService.cs:162` |
| ChatView 스트리밍 | try-catch → 메시지에 에러 추가 + Snackbar | `ChatView ~:966` |
| Git 워크플로우 | `session.ErrorMessage` 문자열 | `SessionGitWorkflowService.cs:90` |
| 훅 엔진 | try-catch → 로그 경고 | `HooksEngine.cs:107` |
| SpotlightService | try-catch → 로그 경고 | 전체 |

### 빠진 것 / 문제점
- **중앙 에러 전략 없음**: 각 서비스가 독자적 방식
- **에러 코드/타입 없음**: `session.ErrorMessage`가 자유 형식 문자열. UI가 "git push 실패"와 "워크트리 생성 실패"를 구분 못함
- **transient vs permanent 구분 없음**: 네트워크 타임아웃과 잘못된 설정을 같은 방식으로 처리
- **async void 예외 미관찰**: `HandleStateChanged`가 `async void` 패턴
- **프로세스 stderr 파싱 ad-hoc**: git/gh/claude 각각 다른 방식으로 에러 텍스트 해석

---

## 25. 의존성 그래프 & 커플링

### DI 의존성 (주입 받는 서비스 수)

```
ChatView.razor          ← 18개 서비스 (최대 커플링)
SessionList.razor       ← 10+ 서비스
SessionService          ← 6개 (Git, Workspace, Settings, Context, Hooks, Logger)
SessionGitWorkflow      ← 6개 (Session, Git, Gh, Workspace, Hooks, Logger)
ClaudeService           ← 3개 (Settings, Shell, Logger)
ChatState               ← 0개 (의존 없음, 모든 UI 컴포넌트가 의존)
```

### 서비스간 호출 그래프

```
ChatView ───→ ClaudeService ───→ SettingsService
    │                              ↑
    ├───→ SessionService ──────────┤
    │         │                    │
    │         ├───→ GitService     │
    │         ├───→ WorkspaceService ──→ GitService
    │         ├───→ ContextService │
    │         └───→ HooksEngine ───→ ShellService
    │                              │
    ├───→ SessionGitWorkflow ──────┤
    │         ├───→ SessionService │
    │         ├───→ GitService     │
    │         ├───→ GhService      │
    │         └───→ HooksEngine    │
    │                              │
    ├───→ ChatState (직접 참조)    │
    ├───→ MemoryService            │
    ├───→ UsageService             │
    └───→ AttachmentService        │
```

### 빠진 것 / 문제점
- **ChatView가 커플링 허브**: 18개 서비스 주입. 서비스 변경 → ChatView 수정 필요
- **미디에이터 패턴 없음**: 모든 통신이 직접 서비스 호출 + ChatState.OnChange
- **같은 세션을 여러 경로로 로드**: ChatView와 SessionGitWorkflow가 독립적으로 `LoadSessionAsync` 호출. 다른 인스턴스를 가지고 작업할 수 있음
- **테스트 0개**: 전체 코드베이스에 단위/통합 테스트 없음. 모든 리팩터링이 수동 검증 필요

---

# 핵심 흐름 다이어그램

## 메시지 전송 흐름

```
[사용자] ──→ InputArea.razor
                │
                │ OnSend 이벤트
                ▼
         ChatView.HandleSend()
                │
                ├─ (Pending 세션이면) SessionService.InitializeWorktreeAsync()
                │     └─ GitService.AddWorktreeAsync() → 워크트리 + 브랜치 생성
                │
                ├─ 첨부파일 처리 (AttachmentService)
                ├─ 스킬 확장 (/command → 프롬프트)
                ├─ ChatState.AddUserMessage() → 메시지 목록에 추가
                │
                ▼
         ChatView.ProcessMessageAsync() [Task.Run]
                │
                ├─ (첫 메시지면) ClaudeService.SummarizeAsync() → 제목 생성
                │     └─ GitService.RenameBranchAsync() → 브랜치 이름 변경
                │
                ├─ BuildSystemPromptAsync()
                │     ├─ Workspace.SystemPrompt
                │     ├─ Plan Mode 프롬프트
                │     ├─ ContextService.LoadContextAsync()
                │     └─ MemoryService.GetAllAsync()
                │
                ├─ ChatState.StartAssistantMessage()
                │
                ▼
         ClaudeService.SendMessageAsync() → IAsyncEnumerable<StreamEvent>
                │
                │ [스트리밍 루프]
                ├─ StreamEvent 타입별 처리 (12종)
                │     ├─ content_block_start → ChatState.AddToolCall/SetPhase
                │     ├─ content_block_delta → ChatState.AppendText/AppendThinking
                │     ├─ content_block_stop  → 중간 저장 (SessionService.SaveSessionAsync)
                │     ├─ result              → 사용량 기록 (UsageService)
                │     └─ error               → 에러 텍스트 추가
                │
                ▼
         [finally 블록]
                ├─ ChatState.FinishMessage()
                ├─ ChatState.SetStreaming(false)
                ├─ Plan 모드 감지 (3계층)
                ├─ 질문 감지 (QuestionDetector)
                ├─ SessionService.SaveSessionAsync()
                ├─ HooksEngine.FireAsync(OnMessageComplete)
                └─ PR 상태 체크 (CheckAndUpdatePrStatus)
```

## 세션 생명주기

```
                    CreatePendingSessionAsync()
                            │
                            ▼
                    ┌──────────────┐
                    │   Pending    │ (워크트리 없음)
                    └──────┬───────┘
                           │ 첫 메시지 전송
                           ▼
                    InitializeWorktreeAsync()
                           │
                           ▼
                    ┌──────────────┐
              ┌─────│    Ready     │←──── RetryAfterConflictResolveAsync()
              │     └──────┬───────┘                ▲
              │            │ PushBranchAsync()       │
              │            ▼                        │
              │     ┌──────────────┐               │
              │     │   Pushed     │               │
              │     └──────┬───────┘               │
              │            │ CreatePrAsync()         │
              │            ▼                        │
              │     ┌──────────────┐               │
              │     │   PrOpen     │               │
              │     └──┬───────┬───┘               │
              │        │       │                    │
              │  성공   │       │ 충돌               │
              │        ▼       ▼                    │
              │  ┌─────────┐ ┌────────────────┐    │
              │  │ Merged  │ │ConflictDetected│────┘
              │  └────┬────┘ └────────────────┘
              │       │
              │       │ CleanupSessionAsync()
              │       ▼
              │  ┌──────────────┐
              └──│  Archived    │
                 └──────────────┘

         ※ 어디서든 Error 상태로 전환 가능 (검증 없음)
```

---

# 구조적 문제 Top 10 (영향도 순위)

| 순위 | 문제 | 영향 | 관련 파일 |
|------|------|------|-----------|
| **1** | ChatView God Component (1,461줄, 18개 서비스) | 모든 기능 변경이 하나의 파일에 집중 | `ChatView.razor` |
| **2** | JSON 영속화에 파일 락 없음 | 병렬 세션의 동시 저장 시 데이터 손상 | `SessionService.cs` |
| **3** | 세션 파일에 전체 메시지 저장 | 무한 성장, 로드 성능 저하 | `Session.cs`, `SessionService.cs` |
| **4** | ChatState God Object (30+ 멤버) | 5가지 관심사 혼재, 테스트 불가 | `ChatState.cs` |
| **5** | 모델 검증 완전 부재 | 잘못된 상태 전이, 데이터 무결성 미보장 | 모든 `Models/` |
| **6** | 프로세스 실행 4곳 복붙 | 타임아웃/에러처리 불일치 | Git/Gh/Claude/Hooks |
| **7** | SessionList God Component (719줄) | 사이드바 변경이 복잡 | `SessionList.razor` |
| **8** | PR 생성 경로 2개 (AI vs 직접) | 결과 불일치 가능 | `ChatView.razor`, `SessionGitWorkflowService.cs` |
| **9** | 가격/모델/CLI플래그 하드코딩 | 외부 변경마다 코드 수정 필요 | `UsageService.cs`, `ModelDefinitions.cs`, `ClaudeArgumentBuilder.cs` |
| **10** | 테스트 0개 | 리팩터링 위험, 회귀 감지 불가 | 전체 |

---

> 이 문서의 각 섹션을 가리켜 변경 방향을 지시하세요. 예:
> - "섹션 10의 ChatView를 분해해야 함: 스트림 처리를 `StreamEventProcessor` 서비스로, PR 워크플로우를 별도 컴포넌트로"
> - "섹션 3의 Session 모델을 분리해야 함: 대화 관련 / Git 관련 / PR 관련을 각각의 모델로"
> - "섹션 2의 영속화를 개선해야 함: 메시지를 별도 파일로 분리하고 파일 락 추가"
