# Cominomi 해부 문서 (Anatomy)

> 이 문서는 Cominomi의 모든 시스템을 해부하여 **현재 동작**, **데이터 흐름**, **의존관계**, **빠진 것/문제점**을 기술합니다.
> 각 섹션을 가리켜 "이 부분은 이렇게 변경되어야 한다"고 지휘하는 데 사용하세요.

**코드 규모**: ~235개 소스 파일, ~24,246줄 (Cominomi.Shared에 집중, tests/Cominomi.Shared.Tests 포함)
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
- [미해결 구조적 문제 Top 10](#미해결-구조적-문제-top-10)

---

# Part I: 기반 레이어

## 1. 앱 부트스트랩 & DI

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `src/Cominomi/MauiProgram.cs` | 156 | DI 컨테이너 전체 설정 + Spotlight 복구 + 플러그인 엔진 초기화 + 옵션 패턴 |
| `src/Cominomi/App.xaml.cs` | 41 | MAUI App 수명주기 + Graceful shutdown (서비스 Dispose) |
| `src/Cominomi/MainPage.xaml` | 14 | BlazorWebView 호스트 |
| `src/Cominomi/Components/Routes.razor` | 8 | Blazor 라우팅 |

### 현재 동작
`MauiProgram.cs:17-154`에서 앱이 부트스트랩됨:

1. **Serilog 초기화** (`:19-36`): 콘솔 + 롤링 파일 로그 (14일 보관). Debug 빌드 시 Debug 레벨 + Debug 출력 추가
2. **MAUI 설정** (`:38-44`): BlazorWebView + OpenSans 폰트
3. **MudBlazor** (`:52-62`): Snackbar 설정 (하단 우측, 3초, 최대 3개, 중복 방지)
4. **옵션 패턴 설정** (`:64-69`): `AppSettingsChangeNotifier` + `IOptionsFactory<AppSettings>` 등록
5. **서비스 등록** (`:72-116`): **45개 전부 Singleton** (옵션 패턴 헬퍼 + 핸들러 레지스트리 포함)
6. **모델 정의 로딩** (`:118-120`): `models.json` 외부 파일에서 가격/모델 정보 로드
7. **Spotlight 크래시 복구** (`:129-138`): 앱 시작 시 `ISpotlightService.RecoverAsync()` 호출 — 이전 실행에서 비정상 종료된 Spotlight 상태 자동 복원
8. **플러그인 엔진 초기화** (`:140-152`): `IPluginService` ↔ `IPluginExecutionEngine` 연결 + 활성 플러그인 로드

```
등록 순서 (MauiProgram.cs:72-116):
  IShellService         → ShellService
  IChatState            → ChatState
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
  IPluginExecutionEngine → PluginExecutionEngine        ← Phase 6+ 추가
  IUsageService         → UsageService
  IMcpService           → McpService
  INotificationService  → NotificationService (플랫폼별)
  INotificationHistoryService → NotificationHistoryService ← 추가
  IActivityService      → ActivityService
  IStreamEventHandler   → 10개 핸들러 (핸들러 레지스트리 패턴) ← Phase 16 변경
  IStreamEventProcessor → StreamEventProcessor
  IProcessRunner        → ProcessRunner
  ISystemPromptBuilder  → SystemPromptBuilder          ← Phase 3 추가
  ISessionInitializer   → SessionInitializer           ← Phase 3 추가
  IChatPrWorkflowService → ChatPrWorkflowService       ← Phase 3 추가
  IChatMessageOrchestrator → ChatMessageOrchestrator   ← Phase 16 추가
  SessionListDataService → SessionListDataService      ← Phase 4 추가
  ISessionListFacade    → SessionListFacade            ← 추가
  IThemeService         → ThemeService                 ← 추가
```

**스트림 이벤트 핸들러 레지스트리** (`:98-107`): `IStreamEventHandler` 10개를 DI 다중 등록하여 `StreamEventProcessor`가 이벤트 타입별로 디스패치:
`SystemInitHandler`, `ContentBlockStartHandler`, `ContentBlockDeltaHandler`, `ContentBlockStopHandler`, `AssistantMessageHandler`, `UserMessageHandler`, `MessageStartHandler`, `MessageDeltaHandler`, `ResultHandler`, `ErrorHandler`

**Graceful Shutdown** (`App.xaml.cs:21-40`): `CleanUp()` 오버라이드로 서비스 정리:
- `IClaudeService` Dispose — 활성 CLI 프로세스 종료
- `ISpotlightService` Dispose — Spotlight 상태 정리
- `ChatState` Dispose — 리소스 해제
- `SessionListDataService` Dispose — 캐시 정리
- `Log.CloseAndFlush()` — Serilog 버퍼 플러시

`MainPage.xaml`은 `<BlazorWebView>`를 호스트하며, `Routes.razor`가 `MainLayout`으로 라우팅.

---

## 2. 영속화 레이어 (JSON 파일 스토리지)

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/AppPaths.cs` | 24 | 디렉토리 경로 정의 (EnsureDir 자동 생성) |
| `Shared/Services/JsonDefaults.cs` | 12 | 공유 직렬화 옵션 |
| `Shared/Services/AtomicFileWriter.cs` | - | 임시파일→원자적 이동 패턴 (Write + Append) |
| `Shared/Services/SettingsService.cs` | 48 | settings.json 캐시 + MigratingJsonReader/Writer + AtomicFileWriter |
| `Shared/Services/UsageService.cs` | 370 | usage.jsonl — 중복 제거 + 자동 로테이션 + CSV 내보내기 + 비용 계산 |
| `Shared/Services/Migration/` | - | 스키마 마이그레이션 인프라 (`MigratingJsonReader`, `MigratingJsonWriter`, `SchemaMigrator` 등) |

### 현재 동작

**디렉토리 구조** (`AppPaths.cs:5-17`):
```
%APPDATA%/Cominomi/
├── settings.json          ← SettingsService (인메모리 캐시)
├── hooks.json             ← HooksEngine (엔벨로프 형식: {$schemaVersion, hooks})
├── sessions/              ← SessionService
│   ├── {uuid}.json        ← 세션 메타데이터만 (Phase 4에서 분리)
│   └── {uuid}.messages.json ← 메시지 별도 저장 (Phase 4 추가)
├── workspaces/            ← WorkspaceService
│   └── {uuid}.json
├── repos/                 ← WorkspaceService (리포 메타데이터)
│   └── {uuid}.json
├── memory/                ← MemoryService
│   └── {uuid}.json
├── tasks/                 ← TaskService
│   └── {uuid}.json
├── usage.jsonl            ← UsageService (AppPaths.Usage로 통합)
└── archived-contexts/     ← SessionService (아카이브)
    └── {workspaceName}/{sessionName}/.context/
```

**쓰기 패턴**: `AtomicFileWriter`를 사용한 원자적 쓰기 — 임시 파일에 쓴 후 `File.Move()`로 교체. `AppendAsync()`도 지원하여 JSONL 추가 쓰기에도 적용.

**읽기 패턴**: `MigratingJsonReader.Read<T>(json, options)` — 역직렬화 시 `$schemaVersion` 검사 → 필요 시 자동 마이그레이션 → write-back용 JSON 반환. 레거시 서비스는 직접 `JsonSerializer.Deserialize` 사용.

**SettingsService** (`SettingsService.cs`):
- `LoadAsync()`: `MigratingJsonReader` 사용, `ModelDefinitions.NormalizeModelId()` 정규화, 마이그레이션 발생 시 즉시 AtomicFileWriter로 write-back
- `SaveAsync()`: `MigratingJsonWriter.Write()` → `AtomicFileWriter.WriteAsync()` → `AppSettingsChangeNotifier.NotifyChanged()` 발행 (IOptionsMonitor 갱신 트리거)

**UsageService** (`UsageService.cs:370줄`):
- **SHA256 기반 중복 제거**: `DedupHash` 필드로 엔트리별 해시 영속화. 앱 재시작 시 기존 해시 로드하여 중복 방지
- **SemaphoreSlim 쓰기 잠금**: 동시 쓰기 보호
- **자동 로테이션**: 10MB 초과 시 90일 이전 엔트리 자동 제거 + `AtomicFileWriter`로 재작성
- **비용 계산**: `ModelDefinitions.GetPricing()` 위임 — 입력/출력/캐시 생성/캐시 읽기 토큰별 단가 적용
- **통계 집계**: 모델별/날짜별/프로젝트별 `UsageStats` 생성
- **CSV 내보내기**: `ExportCsvAsync()` — 지정 기간의 엔트리를 CSV 형식으로 반환
- **수동 퍼지**: `PurgeOldEntriesAsync()` — 보존 기간 지정 가능 (기본 90일)

**세션 파일 구조** (Phase 4에서 분리):
- `SaveSessionAsync()`: 메타데이터(`{uuid}.json`)와 메시지(`{uuid}.messages.json`)를 별도 파일로 저장. `ToolCall.Output`이 2,000자 초과 시 `[truncated, N chars]`로 절단
- `GetSessionsAsync()`: 메타데이터 파일만 읽어 경량 목록 생성 (`.messages.json` 파일 제외)
- `LoadSessionAsync()`: 메타데이터 로드 후 `.messages.json`에서 메시지 별도 로드 + `MigrateToParts()` 호출. 이전 단일 파일 형식도 자동 호환 (메시지가 인라인에 있으면 그대로 사용)

---

## 3. 데이터 모델

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Models/Session.cs` | 89 | 핵심 엔티티 + 상태 전이 검증 + `init` 불변성, `Git`/`Pr` 서브 객체로 관심사 분리 |
| `Shared/Models/SessionStatusMachine.cs` | 27 | 상태 전이 규칙 정의 + 유효성 검증 |
| `Shared/Models/GitContext.cs` | 14 | Git worktree/branch 관련 속성 그룹 |
| `Shared/Models/PrContext.cs` | 14 | PR/이슈 관련 속성 그룹 |
| `Shared/Models/SessionJsonConverter.cs` | 240 | 기존 플랫 JSON ↔ 신규 중첩 JSON 하위호환 컨버터 + `$schemaVersion` 스탬핑 |
| `Shared/Models/Workspace.cs` | 46 | 리포 설정 + 스크립트 + 선호도 |
| `Shared/Models/ChatMessage.cs` | 53 | 메시지 + Parts + 첨부파일 + 스트리밍 시간 |
| `Shared/Models/StreamEvent.cs` | 165 | Claude CLI 스트림 프로토콜 |
| `Shared/Models/ToolCall.cs` | 11 | 도구 호출 기록 |
| `Shared/Models/AppSettings.cs` | 39 | 앱 설정 + 요약 모델/프롬프트 + 타임아웃 + 플러그인 설정 |
| `Shared/CominomiConstants.cs` | 53 | 공유 상수 (기본값, 토큰 한도, 환경변수 블록) |
| `Shared/Models/AppError.cs` | - | 에러 레코드 + `ErrorCode` enum + 에러 분류 |
| `Shared/Models/ViewModels/` | - | UI 전용 모델 (`ContentGroup`, `ActivitySummaryInfo`, `MainTab`, `PlanReviewAction`) |

### Session (핵심 엔티티)

`Session.cs` — 3가지 관심사를 `GitContext`/`PrContext` 서브 객체로 분리. `init` 프로퍼티로 생성 후 불변 보장:

```
[대화 관심사 — Session 루트]
  Id(init), Title, Messages, Model, PermissionMode, EffortLevel, AgentType(init)
  CityName(init), ConversationId, MaxTurns(init), MaxBudgetUsd(init)
  TotalInputTokens(Guard), TotalOutputTokens(Guard), WorkspaceId(init)
  Status(private set), Error(AppError?), PlanCompleted, PlanFilePath
  CreatedAt(init), UpdatedAt, ResolvedModel([JsonIgnore])

[Git 관심사 — session.Git (GitContext)]
  WorktreePath, BranchName, BaseBranch, IsLocalDir, AdditionalDirs

[PR/이슈 관심사 — session.Pr (PrContext)]
  PrUrl, PrNumber, IssueNumber, IssueUrl, ConflictFiles
```

**SessionJsonConverter** (240줄):
- **읽기**: 기존 플랫 포맷(v1)과 중첩 포맷(v2) 모두 역직렬화 지원. `init` 프로퍼티는 객체 이니셜라이저로, 가변 프로퍼티는 생성 후 할당
- **쓰기**: 항상 중첩 포맷 + `$schemaVersion` 자동 스탬핑 (`SchemaMigratorRegistry`에서 현재 버전 조회)
- **Error 하위호환**: 구조화 `AppError` 객체 우선, 레거시 `errorMessage` 문자열 폴백

### 상태 머신 (SessionStatus)

`Session.cs:8-19` — 9가지 상태. `SessionStatusMachine`(27줄)으로 전이 규칙 명시 + `Session.Status`를 `private set`으로 보호:

```
Pending → Initializing, Ready, Error
Initializing → Ready, Error
Ready → Pushed, PrOpen, Merged, Error, Archived
Pushed → PrOpen, Merged
PrOpen → Merged, ConflictDetected, Ready
ConflictDetected → Ready
Merged → Archived
Error → Ready, Initializing, Archived
```

- `Session.TransitionStatus(target)`: 유효 전이만 허용, 무효 시 `InvalidOperationException`
- `Session.SetInitialStatus(status)`: 역직렬화 시 검증 우회
- 동일 상태 전이(idempotent)는 항상 허용

### ChatMessage 이중 저장

`ChatMessage.cs:17-46`:
```csharp
public string Text { get; set; }           // 레거시: 전체 텍스트
public List<ToolCall> ToolCalls { get; init; } = [];  // 레거시: 도구 호출 목록 (init)
public List<ContentPart> Parts { get; init; } = [];   // 신규: 인터리브 렌더링 (init)
public List<FileAttachment> Attachments { get; init; } = [];  // 첨부파일
```

`MigrateToParts()` (`:36-45`): 매 로드마다 호출. Parts가 비어있으면 ToolCalls→Parts, Text→Parts로 변환.
스트리밍 시간 추적: `StreamingStartedAt`, `StreamingFinishedAt` → `Duration` 계산 프로퍼티.

### AppSettings (39줄)

```
[기본값] DefaultModel, Theme, ClaudePath, DefaultCloneDirectory, DefaultEffortLevel, DefaultPermissionMode
[세션 한도] DefaultMaxTurns, DefaultMaxBudgetUsd, FallbackModel
[요약] SummarizationModel("haiku"), SummarizationPrompt
[타임아웃] DefaultProcessTimeoutSeconds(30), HookTimeoutSeconds(5),
          SummarizationTimeoutSeconds(15), VersionCheckTimeoutSeconds(5)
[알림] NotificationsEnabled, NotificationSound
[플러그인] DisabledPlugins
[기타] McpConfigPath, DebugMode, EnvironmentVariables, DefaultMergeStrategy,
       DefaultPrBodyTemplate, LastWorkspaceId, LastSessionId
```

### CominomiConstants (53줄)

```
[앱] AppName, BranchPrefix("cominomi/")
[기본값] DefaultPermissionMode("bypassAll"), DefaultEffortLevel("auto"), DefaultMergeStrategy("squash")
[토큰 한도] MaxContextPromptTokens(5,000), MaxContextItemTokens(2,000),
            MaxMemoryPromptTokens(2,500), MaxMemoryEntryTokens(1,000),
            MaxSystemPromptTokens(10,000), TruncationMarker
[환경변수 — Env 내부 클래스]
  NoColorEnv: { NO_COLOR=1 }
  GitEnv: { GIT_TERMINAL_PROMPT=0, NO_COLOR=1 }
  GhEnv: { GH_NO_UPDATE_NOTIFIER=1, NO_COLOR=1 }
  HookEvent: COMINOMI_HOOK_EVENT
```

### Workspace (46줄)

기존 리포 설정에 스크립트/선호도 확장:
- `SetupScript`, `RunScript`, `ArchiveScript` — 워크스페이스별 자동화 스크립트
- `CodeReviewPreferences`, `CreatePrPreferences`, `BranchRenamePreferences`, `GeneralPreferences` — 워크스페이스별 AI 지침
- `Error` → `AppError?` 구조화 에러 (레거시 `ErrorMessage`는 `[JsonIgnore]` 계산 프로퍼티)

---

# Part II: 외부 도구 통합

## 4. 셸 서비스 & 프로세스 실행

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/ShellService.cs` | 187 | 셸 감지, WhichAsync, 10분 TTL 캐시 + InvalidateCache |
| `Shared/Services/IShellService.cs` | 28 | 인터페이스 (InvalidateCache 추가) |

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

### 프로세스 실행 패턴 — `IProcessRunner` 통합 완료

모든 프로세스 실행 서비스가 `IProcessRunner.RunAsync()`를 사용:

| 서비스 | 마이그레이션 전 | 마이그레이션 후 |
|--------|----------------|----------------|
| GhService | `RunGhAsync()`: Process 직접 생성 | ✅ `IProcessRunner` + `CominomiConstants.Env.GhEnv` |
| GitService | `CreateGitProcess()` + `RunGitAsync()` | ✅ `IProcessRunner` + `CominomiConstants.Env.GitEnv` |
| HooksEngine | `Process.Start()` + 수동 타임아웃 | ✅ `IProcessRunner` + `CominomiConstants.Env.HookEvent` |
| ShellService | `Process` 직접 생성 (WhichAsync, FindGitBashAsync) | ✅ `IProcessRunner` |
| DependencyCheckService | `Process` 직접 생성 (GetVersionAsync) | ✅ `IProcessRunner` |
| ClaudeCliResolver | `Process` 직접 생성 (RunSimpleCommandAsync) | ✅ `IProcessRunner` + `CominomiConstants.Env.NoColorEnv` |
| McpService | `Process` 직접 생성 (RunClaudeCommandAsync) | ✅ `IProcessRunner` |

**예외**: `ClaudeService.StartProcess()`와 `GitService.CloneAsync()`는 stdin 스트리밍 / 실시간 stderr 진행률 보고가 필요하여 `Process`를 직접 사용. 환경변수는 `CominomiConstants.Env`로 통합.

---

## 5. Git 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/GitService.cs` | 648 | 모든 git 명령 (`IProcessRunner` 기반) + 설정 가능 git 경로. ParseDiff 삭제(708→648) |
| `Shared/Services/IGitService.cs` | 33 | 인터페이스 |

### 현재 동작

**핵심 메서드**:
- `CloneAsync()` (`:17-85`): progress 리포트 포함. CancellationToken 지원
- `AddWorktreeAsync()` (`:87-100`): 브랜치 존재 여부 확인 후 분기
- `RemoveWorktreeAsync()` (`:102-117`): `--force` + 디렉토리 수동 삭제 + prune
- `DetectDefaultBranchAsync()` (`:119-139`): symbolic-ref → main 확인 → master 확인 → 현재 브랜치
- `PushBranchAsync()` (`:215-218`): `git push -u origin`
- `PushForceBranchAsync()` (`:220-223`): `git push --force-with-lease origin`
**프로세스 실행** (`RunGitAsync()`, `:186-197`):
`IProcessRunner.RunAsync()`를 통해 `git` 명령 실행. 인수는 배열(`params string[]`)로 전달하며 `ArgumentList`를 사용하여 셸 해석 없이 안전하게 전달. 환경변수는 `CominomiConstants.Env.GitEnv`로 통합.

**예외**: `CloneAsync()`는 stderr 진행률 실시간 보고가 필요하여 `CreateStreamingGitProcess()`로 직접 `Process` 사용.

### 데이터 흐름
```
SessionService ──→ GitService.AddWorktreeAsync() (세션 생성)
SessionService ──→ GitService.RemoveWorktreeAsync() (세션 정리)
SessionGitWorkflowService ──→ GitService.PushBranchAsync() (브랜치 푸시)
SidebarExplorer ──→ GitService.ListTrackedFilesAsync() (파일 목록)
SidebarChanges ──→ GitService.GetDiffSummaryAsync() (변경사항)
ChatView ──→ GitService.RenameBranchAsync() (제목 기반 브랜치 이름 변경)
```

---

## 6. GitHub CLI 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/GhService.cs` | 253 | PR/이슈 CRUD + CI 체크 대기 + rate limit 재시도 |
| `Shared/Services/IGhService.cs` | 24 | 인터페이스 (WaitForChecksAsync 추가) |

### 현재 동작

```
CreatePrAsync()  (GhService.cs:16-23)  → gh pr create --base --head --title --body
MergePrAsync()   (GhService.cs:25-30)  → gh pr merge {number} --{method}
GetPrForBranchAsync() (GhService.cs:32-55) → gh pr view {branch} --json number,url,state
IsAuthenticatedAsync() (GhService.cs:57-61) → gh auth status (workingDir=".")
ListIssuesAsync() (GhService.cs:72-100)    → gh issue list --json --limit 30
```

---

## 7. Claude CLI 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/ClaudeService.cs` | 422 | CLI 프로세스 관리 + 스트리밍 + 크래시 복구 |
| `Shared/Services/IClaudeService.cs` | 34 | 인터페이스 |
| `Shared/Services/ClaudeArgumentBuilder.cs` | 119 | CLI 인자 구성 |
| `Shared/Services/ClaudeCliResolver.cs` | 112 | npx/직접실행 감지 |
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
7. --verbose 필요하면 재시도 (`ExecuteClaudeProcessAsync` 재호출)
8. stderr에 에러 있으면 합성 error 이벤트 yield + 비정상 종료 시 크래시 복구 이벤트
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

### 설계 특성
- **메시지당 프로세스 생성**: Claude CLI `--print` 모드는 단발 실행. 매 메시지마다 새 프로세스를 생성하고, `--resume {conversationId}`로 대화 맥락을 유지. 이전 세션 프로세스는 `_agents` 딕셔너리로 추적하여 중복 실행 방지 (`ConcurrentDictionary<string, AgentProcess>`)

### 빠진 것 / 문제점
(없음)

---

# Part III: 워크플로우 오케스트레이션

## 8. 세션 생명주기

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/SessionService.cs` | 677 | 세션 CRUD + 워크트리 + 파일 분리 저장 + per-session SemaphoreSlim 동시성 보호 + 워크스페이스당 세션 수 제한 |
| `Shared/Services/SessionStatusMachine.cs` | 27 | 상태 전이 규칙 (Phase 4) |
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

---

## 9. Git 워크플로우 파이프라인

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/SessionGitWorkflowService.cs` | 359 | Push→PR→Merge 파이프라인 + 자동 리베이스 + PR 닫기 |
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

---

## 10. ChatView — 중앙 오케스트레이터

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Components/Chat/ChatView.razor` | ~464 | UI 오케스트레이터 (Phase 16에서 697→424줄→464줄. 비즈니스 로직은 ChatMessageOrchestrator로 추출, Phase 17에서 태스크 관찰 추가) |
| `Shared/Services/ChatMessageOrchestrator.cs` | ~230 | 메시지 전송/Continue 비즈니스 로직 (Phase 16 추출) |
| `Shared/Services/IChatMessageOrchestrator.cs` | ~52 | 인터페이스 + StreamResult DTO |
| `Shared/Components/Chat/BranchSelector.razor` | ~106 | 브랜치 선택 UI (Phase 6 추출) |
| `Shared/Components/Chat/SessionWorkflowBar.razor` | ~205 | PR/워크플로우 바 (Phase 6 추출) |
| `Shared/Components/Chat/LandingPage.razor` | ~93 | 랜딩 페이지 (Phase 6 추출) |

### 현재 동작

**주입 서비스 10개** (Phase 14에서 13→10개 축소):
```
IChatState, IChatMessageOrchestrator, ClaudeService,
SessionService, SessionInitializer, JSRuntime,
Logger, Snackbar, NotificationService
```
> Phase 16 제거: AttachmentService, HooksEngine, StreamEventProcessor, SystemPromptBuilder, ChatPrWorkflowService → `IChatMessageOrchestrator`로 통합
> Phase 16 추가: IChatMessageOrchestrator (메시지 전송/Continue 오케스트레이션)

**이 컴포넌트가 담당하는 것들**:

| 기능 | 줄 범위 (대략) | 설명 |
|------|----------------|------|
| 랜딩 페이지 | (추출됨) | `LandingPage.razor` — 세션 없을 때 워크스페이스 생성 + 최근 대화 |
| 브랜치 선택 | (추출됨) | `BranchSelector.razor` — Pending 세션의 브랜치 피커 (검색/필터) |
| 워크플로우 바 | (추출됨) | `SessionWorkflowBar.razor` — PR 생성/병합/충돌 해결/강제 푸시 버튼 |
| 메시지 렌더링 | 마크업 영역 | MessageBubble 루프 + 스트리밍 인디케이터 |
| 입력 영역 | 마크업 영역 | InputArea 래핑 + 이벤트 핸들링 |
| 메시지 전송 | `HandleSend` | `IChatMessageOrchestrator.SendAsync()`에 위임 |
| Continue | `HandleContinue` | `IChatMessageOrchestrator.ContinueAsync()`에 위임 |
| 스트림 처리 | (추출됨) | `ChatMessageOrchestrator` → `StreamEventProcessor` 위임 체인 |
| 사용량 추적 | (추출됨) | `StreamEventProcessor.FinalizeAsync()`로 캡슐화 |
| 플랜 모드 | (추출됨) | `StreamEventProcessor.FinalizeAsync()`로 캡슐화 |
| 질문 감지 | finally 블록 | QuestionDetector → QuickResponseBar |
| 시스템 프롬프트 | (추출됨) | `ISystemPromptBuilder.BuildAsync()`로 위임 |
| 브랜치 로딩/요약 | (추출됨) | `ISessionInitializer`로 위임 |
| PR 워크플로우 | (추출됨) | `IChatPrWorkflowService`로 위임 (UI만 남음: Snackbar, 상태 갱신) |
| 알림 | 별도 메서드 | 윈도우 포커스 감지 + 조건부 토스트 |

### ProcessMessageAsync 스트림 처리 switch문 → `StreamEventProcessor`로 이동

> 아래 switch 로직은 현재 `StreamEventProcessor.cs`(516줄)에 위치. ChatView는 `_streamProcessor.ProcessEventAsync()`를 1줄 호출.

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

---

## 10.5 스트림 이벤트 프로세서

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/StreamEventProcessor.cs` | ~110 | 핸들러 레지스트리 기반 디스패처 + FinalizeAsync 후처리 |
| `Shared/Services/IStreamEventProcessor.cs` | 47 | 인터페이스 + `StreamProcessingContext` DTO |
| `Shared/Services/StreamEventHandlers/IStreamEventHandler.cs` | 19 | 핸들러 인터페이스 (`EventType` + `HandleAsync`) |
| `Shared/Services/StreamEventHandlers/StreamEventUtils.cs` | 110 | 공유 유틸리티 (ExtractToolResultContent, RecordUsageAsync 등) |
| `Shared/Services/StreamEventHandlers/*Handler.cs` | 각 15~90 | 이벤트 타입별 핸들러 10개 |

### 아키텍처

**핸들러 레지스트리 패턴** (OCP 준수):
- `IStreamEventHandler` 인터페이스: `EventType` 속성 + `HandleAsync` 메서드
- DI로 10개 핸들러 등록 → `IEnumerable<IStreamEventHandler>`로 주입
- `StreamEventProcessor`가 `Dictionary<string, IStreamEventHandler>`로 O(1) 디스패치
- 새 이벤트 타입 추가 시 기존 코드 수정 없이 핸들러 클래스 추가 + DI 등록만 필요

**핸들러 목록**:
| 핸들러 | EventType | 역할 |
|--------|-----------|------|
| `SystemInitHandler` | system | 모델·세션 ID 초기화 |
| `ContentBlockStartHandler` | content_block_start | thinking/tool_use/tool_result 블록 시작 |
| `ContentBlockDeltaHandler` | content_block_delta | 텍스트/사고/JSON 델타 처리 |
| `ContentBlockStopHandler` | content_block_stop | 도구 완료·세션 저장 |
| `AssistantMessageHandler` | assistant | 완성 메시지 처리 (비스트리밍 경로) |
| `UserMessageHandler` | user | tool_result 매칭 |
| `MessageStartHandler` | message_start | 모델·토큰 초기화 |
| `MessageDeltaHandler` | message_delta | 토큰 누적·max_tokens 감지 |
| `ResultHandler` | result | 사용량 기록 (3단계 폴백)·콘텐츠 복원 |
| `ErrorHandler` | error | 에러 메시지 표시 |

**FinalizeAsync** — 스트림 종료 후 3가지 후처리 (StreamEventProcessor에 유지):
1. **사용량 기록** (폴백): 누적 토큰 → `StreamEventUtils.RecordUsageAsync`
2. **플랜 감지** (3계층): tool_use 기반 → 텍스트 패턴 검색 → 파일 존재 감지
3. **질문 감지**: `QuestionDetector` → `QuickResponseBar` 제안

### 빠진 것 / 문제점
- **ChatState 직접 변경**: 각 핸들러에서 `ChatState`의 가변 상태를 직접 수정. 부수효과 추적 어려움
- **`StreamProcessingContext` 가변 DTO**: 참조 타입으로 컨텍스트 상태 공유. 호출자와 핸들러가 같은 객체를 변경

---

# Part IV: UI 컴포넌트 시스템

## 11. 레이아웃 아키텍처

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Components/Layout/MainLayout.razor` | 225 | 3패널 레이아웃 셸 (테마/의존성 체크 위임, 리사이즈 상태 관리, 파일 콘텐츠 퇴출/리로드) |
| `Shared/Components/Layout/PanelResizer.razor` | 71 | 드래그 리사이즈 핸들 (마우스 + 키보드 화살표 지원, ARIA 접근성) |
| `Shared/Components/Layout/SidebarToolbar.razor` | 69 | 사이드바 브랜드 + 테마/활동/알림(미읽은 뱃지)/사용량/설정 버튼 |
| `Shared/Components/Layout/MainToolbar.razor` | 57 | 메인 툴바 (제목/브랜치/권한모드 뱃지/상태바/패널 토글) |
| `Shared/Components/Layout/DetailPanel.razor` | 45 | 우측 패널 (탐색기/변경사항 전환) |
| `Shared/Components/Layout/MainTabBar.razor` | 31 | 탭 바 (아이콘 + 닫기 버튼) |
| `Shared/Components/Layout/StatusBar.razor` | 21 | 상태 바 (모델 표시 + SpotlightToggle) |
| `Shared/Services/IThemeService.cs` | 12 | 테마 상태 인터페이스 |
| `Shared/Services/ThemeService.cs` | 70 | 테마 상태 관리 (다크/라이트 모드 + MudTheme + 설정 연동) |
| `Shared/Services/TabManager.cs` | 218 | 탭 관리 (LRU 퇴출, 메모리 제한 10MB/50MB, 알림 탭 지원) |

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

**패널 리사이즈** — 사이드바(180–480px)와 디테일 패널(280–700px)을 마우스 드래그 또는 키보드(←/→, Shift로 큰 폭)로 조절 가능. CSS 변수(`--sidebar-width`, `--detail-width`)를 JS interop으로 실시간 갱신. 키보드 단축키: `Ctrl/Cmd+B` 사이드바 토글, `Ctrl/Cmd+]` 디테일 패널 토글.

**MainLayout 책임 분리**: 테마 → `IThemeService`, 사이드바 툴바 → `SidebarToolbar.razor`, 메인 툴바 → `MainToolbar.razor`로 추출. MainLayout에 남은 책임: 3패널 셸 구성, 패널 리사이즈 상태 관리, MudThemeProvider 바인딩, 의존성 체크 → SetupDialog, UsageDashboard 다이얼로그

**TabManager** (`TabManager.cs`, 218줄):
- 타입: Chat, FileContent, FileDiff, Activity, Notifications
- Chat 탭은 항상 존재. 다른 탭은 동적 추가/제거
- 메모리 관리: `MaxSingleFileSizeBytes`(10MB), `MaxTotalContentBytes`(50MB). 초과 시 LRU 퇴출(`EvictLruContent()`)
- `ContentEvicted` 플래그로 퇴출된 파일 콘텐츠 지연 리로드 지원
- `OnTabChanged` 이벤트

### CSS 디자인 토큰 시스템

`src/Cominomi/wwwroot/tokens.css` (73줄) + `app.css` (3,240줄)에 정의된 토큰 시스템:

```
:root {
  /* 스페이싱 (4px 기반 7단계) */
  --space-1: 4px ~ --space-8: 48px

  /* 타이포그래피 (4단계) */
  --text-xs: 0.75rem, --text-sm: 0.8125rem, --text-base: 0.875rem, --text-lg: 1rem

  /* 색상 (다크 모드 기본) */
  --color-bg-base: #1e1e2e, --color-bg-sidebar: #181825, --color-accent: #a78bfa

  /* 보더/그림자/라디어스 */
  --border-default, --shadow-sm, --radius-sm(6px) ~ --radius-full(9999px)

  /* 트랜지션 */
  --transition-fast: 150ms ease
}
[data-theme="light"] { /* 라이트 모드 오버라이드 */ }
```

### 전체 컴포넌트 인벤토리

| 디렉토리 | 개수 | 주요 파일 |
|----------|------|-----------|
| `Activity/` | 1 | ActivityView |
| `Chat/` | 14 | ChatView, InputArea, **InputToolbar** (Phase 17), MessageBubble, LinkIssueDialog, ActivitySummary, PlanReviewBar, QuickResponseBar, StreamingIndicator, ModelSelector, AttachmentChips, BranchSelector, SessionWorkflowBar, LandingPage |
| `Diff/` | 1 | DiffPanel |
| `Files/` | 2 | FileContentView, FileDiffView |
| `Layout/` | 7 | MainLayout, SidebarToolbar, MainToolbar, PanelResizer, DetailPanel, MainTabBar, StatusBar |
| `Notifications/` | 1 | NotificationHistoryView |
| `Settings/` | 7 | SettingsPage, AppSettingsContent, WorkspaceSettingsContent, McpManagerDialog, PluginManagerDialog, SlashCommandsEditor, UsageDashboard |
| `Setup/` | 1 | SetupDialog |
| `Sidebar/` | 8 | SessionList, SessionItem, SessionListToolbar, **AddSessionMenu** (Phase 17), CreateWorkspaceDialog, SidebarExplorer, **FileTreeNode** (Phase 17), SidebarChanges |
| `Spotlight/` | 1 | SpotlightToggle |
| `Tools/` | 7 | ToolCallCard, ToolGroupCard, BashToolWidget, EditToolWidget, GlobToolWidget, GrepToolWidget, ReadToolWidget |
| 기타 | 4 | _Imports, Home, NotFound, Routes |
| **합계** | **54** | |

---

## 12. 사이드바 & 세션 관리 UI

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Components/Sidebar/SessionList.razor` | 367 | 세션 목록 (가상화 적용, ISessionListFacade 파사드 위임) |
| `Shared/Components/Sidebar/SessionItem.razor` | 91 | 세션 행 렌더링 (상태 인디케이터, diff stats 표시) |
| `Shared/Components/Sidebar/SessionListToolbar.razor` | 34 | 툴바 + 검색/필터 |
| `Shared/Components/Sidebar/AddSessionMenu.razor` | 21 | 세션 추가 메뉴 (새 세션 + 로컬 디렉토리) — Phase 17에서 3회 중복 추출 |
| `Shared/Services/SessionListDataService.cs` | 241 | 캐시/정렬/필터/diff stats/merge 상태 체크/프로젝트 그룹화 |
| `Shared/Components/Sidebar/CreateWorkspaceDialog.razor` | 282 | 워크스페이스 생성 (URL 클론 + 로컬 경로 탭) |
| `Shared/Components/Sidebar/SidebarExplorer.razor` | 318 | 파일 트리 오케스트레이션 (FileSystemWatcher, 세션별 확장 상태) |
| `Shared/Components/Sidebar/FileTreeNode.razor` | 85 | 재귀 파일 트리 노드 렌더링 — Phase 17에서 SidebarExplorer에서 추출 |
| `Shared/Components/Sidebar/SidebarChanges.razor` | 210 | 변경사항 뷰 (diff 요약, 파일별 +/- 카운트, FileSystemWatcher) |

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

---

## 13. 채팅 컴포넌트

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Components/Chat/ChatView.razor` | 458 | 채팅 메인 뷰 (메시지 표시, UI 이벤트 핸들링, Orchestrator 위임) |
| `Shared/Components/Chat/MessageBubble.razor` | 286 | 메시지 렌더링 (역할별 아바타, 타임스탬프, 첨부, 활동 요약) |
| `Shared/Components/Chat/InputArea.razor` | 271 | 입력 영역 (텍스트 입력, 첨부, 스킬 체이닝, JS interop) |
| `Shared/Components/Chat/InputToolbar.razor` | 134 | 입력 툴바 (권한/노력/모델/액션 버튼) — Phase 17에서 InputArea에서 추출 |
| `Shared/Components/Chat/SessionWorkflowBar.razor` | 210 | PR 생성/상태 전환/이슈 연결 워크플로우 바 |
| `Shared/Components/Chat/StreamingIndicator.razor` | 122 | 스트리밍 상태 (준비/전송/사고/도구/텍스트 단계 + 경과 시간) |
| `Shared/Components/Chat/PlanReviewBar.razor` | 128 | 플랜 리뷰 (마크다운 미리보기, 승인/거절, 피드백 입력) |
| `Shared/Components/Chat/BranchSelector.razor` | 105 | 브랜치 선택 드롭다운 (필터/검색, 브랜치 그룹) |
| `Shared/Components/Chat/ModelSelector.razor` | 97 | 모델 선택 (빌트인 모델 + 커스텀 입력) |
| `Shared/Components/Chat/LandingPage.razor` | 92 | 랜딩 페이지 (로고, 워크스페이스 생성, 최근 세션) |
| `Shared/Components/Chat/LinkIssueDialog.razor` | 209 | GitHub 이슈 연결 다이얼로그 |
| `Shared/Components/Chat/ActivitySummary.razor` | 141 | 도구 활동 요약 (접기/펴기) |
| `Shared/Components/Chat/AttachmentChips.razor` | 31 | 첨부파일 칩 (이미지 미리보기, 파일 아이콘) |
| `Shared/Components/Chat/QuickResponseBar.razor` | 19 | 빠른 응답 버튼 |
| `Shared/Components/Tools/ToolCallCard.razor` | 153 | 도구 호출 카드 (ReferenceEquals 변경 감지 캐싱) |
| `Shared/Components/Tools/ToolGroupCard.razor` | 92 | 연속 도구 호출 그룹 카드 |
| `Shared/Services/ContentGrouper.cs` | 227 | 파트 그룹화 (구조적 마커 기반 언어 무관 휴리스틱, 도구명 정규화 → ToolDisplayHelper 위임) |
| `Shared/Services/QuestionDetector.cs` | 128 | 질문 감지 (확인/선택/명령형 질문 32패턴 + `?` 폴백) |
| `Shared/Services/ToolDisplayHelper.cs` | 216 | 도구 결과 UI 라벨 + `NormalizeToolName` 단일 소스 (헤더/요약/설명) |

### 현재 동작

**ChatView** (458줄 — Phase 16에서 697→458줄): UI 이벤트 핸들링 전용. 비즈니스 로직은 `IChatMessageOrchestrator`에 위임:
- 메시지 표시 영역 (스크롤, 스트리밍 상태)
- `HandleSend` / `HandleContinue` → Orchestrator 위임
- 스킬 체이닝: `_pendingChain` 필드로 자동 후속 스킬 전송

**MessageBubble**: ContentGrouper로 Parts를 그룹화:
- ToolGroup: 연속된 도구 호출을 접기
- Thinking: 사고 블록 접기
- Text: 최종 텍스트와 중간 텍스트 구분

**InputArea** (271줄) + **InputToolbar** (134줄):
- InputArea: 텍스트 입력 (자동 크기 조절), Enter 전송, 파일 첨부 (드래그/붙여넣기), 스킬 체이닝, "계속" 기능, JS interop
- InputToolbar: 권한 모드 순환, 노력 수준 토글, `ModelSelector` 위임, 액션 버튼 (전송/중지/계속 3상태)

**SessionWorkflowBar** (210줄): Git 세션 전용 워크플로우 바 — PR 생성/상태 전환/이슈 연결

**도구 위젯**: 도구 타입별 전문 렌더링
- `BashToolWidget` (72줄): 명령어 + 출력 (ANSI 컬러 스트립)
- `EditToolWidget` (80줄): 파일 경로 + old/new diff 표시
- `GlobToolWidget` (89줄): 패턴 + 매칭 파일 (접기/더보기)
- `GrepToolWidget` (138줄): 검색 패턴 + 파일별 결과 그룹
- `ReadToolWidget` (94줄): 파일 경로 + 구문 강조 콘텐츠

**ToolDisplayHelper** (216줄 — 정적 유틸리티):
- `NormalizeToolName()`: 도구명 정규화 단일 소스 (ContentGrouper도 위임). MCP 도구명 파싱 포함
- `GetHeaderLabel()`: 도구 입력 JSON에서 컨텍스트 추출 → "Read MessageBubble.razor" 같은 라벨 생성
- `GetCompactResult()`: 출력에서 "50줄 읽음", "3개 파일 일치" 같은 힌트 생성
- `BuildDescriptiveSummary()`: 도구 호출의 서술형 요약

---

# Part V: 확장성 시스템

## 14. 훅 엔진

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/HooksEngine.cs` | 115 | 이벤트 기반 셸 실행 |
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
- **프로젝트 경로 의존**: 워크스페이스 변경 시에만 커스텀 스킬 리로드

---

## 15.5 플러그인 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/PluginService.cs` | ~295 | 플러그인 탐색 + 매니페스트 파싱 + 로드/언로드 위임 |
| `Shared/Services/PluginExecutionEngine.cs` | ~400 | 플러그인 실행 엔진: 로드/언로드/실행 + hooks·skills 등록 + JSON stdin/stdout 프로토콜 |

### 현재 동작
`~/.claude/plugins/` 디렉토리 하위의 각 폴더에서 `manifest.json`을 읽어 플러그인 목록 획득. DI에 `IPluginService` + `IPluginExecutionEngine`으로 등록. `PluginStatus` enum으로 상태 관리 (Discovered, Valid, Invalid, Loaded, Error). 설정에서 enable/disable 가능.

**플러그인 실행 엔진**: 앱 시작 시 활성+유효 플러그인 자동 로드. EntryPoint 파일을 확장자별 인터프리터(`node`, `python3`, `bash` 등)로 자식 프로세스 실행. 프로세스 격리 + 30초 타임아웃으로 샌드박싱. 매니페스트에 `hooks`/`skills` 배열을 선언하면 HooksEngine·SkillRegistry에 자동 등록. 언로드 시 등록 해제.

**JSON stdin/stdout 프로토콜 (`json/1`)**: 실행 시 `PluginRequest` JSON을 stdin으로 전송 (`{pluginId, action, parameters}`). 플러그인이 stdout에 JSON 객체(`{success, message, data}`)를 출력하면 `PluginResponse`로 파싱하여 `PluginExecutionResult.Data`에 반환. JSON이 아닌 plain text stdout도 기존처럼 `Output`으로 사용 가능 (하위호환). 환경변수 `COMINOMI_PROTOCOL=json/1`로 프로토콜 버전 전달.

### 빠진 것 / 문제점
- **디렉토리 없으면 무시**: 사용자에게 플러그인 기능 존재를 알리지 않음

---

## 16. MCP 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/McpService.cs` | ~267 | MCP 서버 관리 (추가/삭제/수정) |
| `Shared/Models/McpServer.cs` | ~21 | 서버 모델 |
| `Shared/Components/Settings/McpManagerDialog.razor` | ~419 | 관리 UI (목록/추가/수정/가져오기) |

### 현재 동작
`~/.claude/mcp.json` + `.claude/mcp.json` JSON 설정 파일 직접 읽기. Command, Args, Env, Url 등 전체 서버 정보 획득. `claude mcp add`, `claude mcp remove`는 여전히 CLI 래핑.

### 빠진 것 / 문제점
- **Windows cmd 셸 호환 문제**: `ImportFromJsonAsync`가 작은따옴표 사용

---

## 17. 컨텍스트 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/ContextService.cs` | ~196 | .context/ 디렉토리 관리 |
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
- **아카이브 경로 충돌**: `workspace.Name`/`session.CityName` 조합이 중복되면 덮어쓰기

---

## 18. 메모리 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/MemoryService.cs` | ~110 | 영구 메모리 + 토큰 기반 크기 제한 (Phase 9) |
| `Shared/Models/MemoryEntry.cs` | ~23 | 메모리 모델 |

### 현재 동작
`%APPDATA%/Cominomi/memory/` 하위에 JSON 파일로 저장. 모든 메모리를 로드하여 `BuildMemoryPrompt()`로 시스템 프롬프트에 주입.

### 빠진 것 / 문제점
- **메모리 검색 없음**: 전체 로드만 가능
- **디렉토리 전체 탐색**: 매번 모든 JSON 파일 읽기

---

# Part VI: 지원 시스템

## 19. 스포트라이트 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/SpotlightService.cs` | ~310 | 워크트리→리포 실시간 동기화 + 크래시 복구 (Phase 4) |

### 현재 동작
```
시작 (Phase 4 개선):
1. 동시 Spotlight 가드 — 이미 활성 세션 있으면 차단
2. spotlight-state.json에 상태 영속화 (sessionId, originalBranch, repoDir)
3. 메인 리포에서 uncommitted 변경 stash (sessionId별 고유 이름)
4. 세션 브랜치로 checkout
5. 워크트리 파일을 메인 리포로 복사
6. FileSystemWatcher로 워크트리 변경 감시 (500ms throttle, flag 기반)
7. 변경 시 자동 동기화

종료:
1. 변경 폐기 (git checkout .)
2. 원래 브랜치로 복귀
3. sessionId 매칭 stash pop
4. spotlight-state.json 삭제

크래시 복구 (RecoverAsync, 앱 시작 시 자동 실행):
1. spotlight-state.json 존재 확인
2. 원래 브랜치로 checkout 복원
3. sessionId별 stash pop 시도
4. spotlight-state.json 삭제
```

### 빠진 것 / 문제점
- **메인 리포 직접 수정**: IDE나 다른 도구와 충돌 가능

---

## 19.5 활동 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/ActivityService.cs` | 101 | 커밋 로그 기반 활동 타임라인 |
| `Shared/Services/IActivityService.cs` | - | 인터페이스 |
| `Shared/Components/Activity/ActivityView.razor` | ~123 | 활동 타임라인 뷰 |

### 현재 동작
`GetWorkspaceActivityAsync()`: 워크스페이스의 활성 세션들에 대해 `Task.WhenAll()`로 병렬 커밋 로그 수집 → 날짜별 그룹화 (`ActivityDateGroup`) → UI 렌더링.

커밋 로그 파싱: `git log --format="%H%x00%h%x00%an%x00%aI%x00%s"` NUL 구분자 파싱.

---

## 20. 사용량 추적 & 비용 계산

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/UsageService.cs` | 365 | JSONL + 비용 계산 + 10MB 로테이션 + CSV 내보내기 |
| `Shared/Models/UsageEntry.cs` | ~77 | 사용량 모델 |
| `Shared/Components/Settings/UsageDashboard.razor` | ~248 | 대시보드 |

### 현재 동작
- JSONL 형식으로 append-only 저장
- SHA-256 기반 중복 제거 (`_seenHashes` 인메모리 HashSet + 첫 쓰기 시 JSONL에서 재로드 + `ComputeEntryHash` 단일 헬퍼로 일관성 보장)
- `ModelDefinitions.GetPricing(modelId)`으로 가격 위임. 기본 내장 가격: Opus $15/$75, Sonnet $3/$15, Haiku $0.80/$4.00 (1M 토큰당). `models.json`으로 변경 가능
- 캐시 할인: write 1.25x, read 0.1x

### 빠진 것 / 문제점
- **대시보드 내보내기 없음**

---

## 21. 알림 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `src/Cominomi/Services/NotificationService.cs` | ~130 | Windows 토스트 + macOS UNUserNotification |
| `Shared/Services/SnackbarExtensions.cs` | 45 | MudBlazor Snackbar 정적 확장 메서드 |

### 현재 동작
`#if MACCATALYST` / `#elif WINDOWS` 조건부 컴파일로 플랫폼별 네이티브 알림 전송. 윈도우 포커스 없을 때 또는 다른 세션 보고 있을 때만 발송.
- **Windows**: `AppNotificationManager` API로 토스트 알림
- **macOS**: `UNUserNotificationCenter` API + `IUNUserNotificationCenterDelegate` — 포그라운드 배너 표시 포함. `NotificationSound` 설정 연동

---

## 22. 설정 시스템

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/SettingsService.cs` | ~38 | JSON 캐시 |
| `Shared/Models/AppSettings.cs` | ~35 | 설정 모델 + 요약/타임아웃 설정 |
| `Shared/Components/Settings/AppSettingsContent.razor` | ~377 | 설정 UI |
| `Shared/Components/Settings/WorkspaceSettingsContent.razor` | ~282 | 워크스페이스 설정 |

### 현재 동작
`settings.json` 1회 로드 → 인메모리 캐시. `SaveAsync()` 시 파일 + `OnSettingsChanged` 이벤트 발사. Phase 7에서 `IOptionsMonitor<AppSettings>` 패턴 도입: `AppSettingsFactory`가 설정 파일에서 인스턴스 생성, `AppSettingsChangeNotifier`가 저장 시 change token 발행. 8개 서비스/컴포넌트가 `IOptionsMonitor<AppSettings>` 주입으로 전환.

### 빠진 것 / 문제점
- **검증 없음**: 잘못된 경로, 음수 값 허용
- **워크스페이스 Preferences가 자유 텍스트**: 구조화된 설정이 아닌 자유 형식 문자열

---

# Part VII: 구조적 분석

## 23. 상태 관리 (ChatState)

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/ChatState.cs` | ~219 | 파사드 (Phase 3에서 366→219줄 감소) |
| `Shared/Services/MessageManager.cs` | ~114 | 메시지 생명주기 관리 (Phase 3 추출) |
| `Shared/Services/StreamingStateManager.cs` | ~66 | 세션별 스트리밍 상태 (Phase 3 추출) |
| `Shared/Services/SettingsStateManager.cs` | ~42 | 설정 UI 상태 (Phase 3 추출) |

### 현재 동작

**컴포지션 아키텍처** (Phase 3):
ChatState가 파사드로 동작하며, 3개 서브 매니저에 위임:

```
ChatState (파사드, 219줄)
  ├─ Messages: MessageManager       — AddUserMessage, StartAssistantMessage, AppendText 등
  ├─ Streaming: StreamingStateManager — 세션별 스트리밍/페이즈/활성 세션 관리
  ├─ Settings: SettingsStateManager  — ShowSettings, SettingsSection 등
  └─ Tabs: TabManager               — 탭 상태 (기존 유지)
```

**하위호환**: 기존 호출부(`ChatState.AddUserMessage()`, `ChatState.SetStreaming()` 등)가 깨지지 않도록 모든 공개 메서드를 1줄 위임으로 유지.

**디바운스** (`NotifyStateChanged()`):
```
스트리밍 중 → 50ms 디바운스 (Timer로 주기적 발사)
스트리밍 아닐 때 → 즉시 발사 + Timer 정리
```

**세션별 스트리밍 상태** (StreamingStateManager):
```csharp
ConcurrentDictionary<string, SessionStreamingState> _streamingStates
ConcurrentDictionary<string, Session> _activeSessions
```

### 빠진 것 / 문제점
- **Session 가변 참조 공유**: `AppendText()`가 `ChatMessage.Text`와 `ChatMessage.Parts`를 직접 수정. ChatState와 Session이 같은 객체를 참조
- **디바운스 Timer 생성/소멸 반복**: 스트리밍 시작/종료 시 Timer를 만들었다 지웠다 함. 전환 시점에 알림 누락 가능
- **ConsumePendingMessage 1회성**: 두 번 호출하면 두 번째는 null. 메시지 손실 가능
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

### ClaudeService 예외 삼킴 (5곳)

`ClaudeService.cs`에서 프로세스 생명주기 관리를 위해 `catch { }` 패턴 사용:
- `:158` — ExitCode 접근 시 프로세스 이미 종료
- `:170` — `process.Dispose()` 실패 무시
- `:224`, `:231` — `process.Kill(entireProcessTree: true)` 실패 무시
- `:334`, `:369` — 재시도/AgentProcess에서 동일 패턴

프로세스 정리 시 예외 발생은 합리적이나, 일괄 무시로 디버깅 정보 손실.

### 빠진 것 / 문제점
- **프로세스 stderr 파싱 ad-hoc**: git/gh/claude 각각 다른 방식으로 에러 텍스트 해석

---

## 25. 의존성 그래프 & 커플링

### DI 의존성 (주입 받는 서비스 수)

```
ChatView.razor          ← 13개 서비스 (Phase 3: 18→14, Phase 6: 14→13)
SessionList.razor       ← 6개 서비스 (Phase 4에서 10+→6개 축소, SessionListDataService로 위임)
ChatPrWorkflowService   ← 6개 (Workspace, GitWorkflow, Gh, Session, Settings, Logger)
SessionService          ← 6개 (Git, Workspace, Settings, Context, Hooks, Logger)
SessionGitWorkflow      ← 6개 (Session, Git, Gh, Workspace, Hooks, Logger)
SystemPromptBuilder     ← 3개 (Context, Memory, Logger)
SessionInitializer      ← 3개 (Git, Claude, Logger)
ClaudeService           ← 3개 (Settings, Shell, Logger)
ChatState (IChatState)  ← 0개 (의존 없음, 모든 UI 컴포넌트가 의존)
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
    ├───→ SystemPromptBuilder      │   ← Phase 3 추가
    │         ├───→ ContextService │
    │         └───→ MemoryService  │
    │                              │
    ├───→ SessionInitializer       │   ← Phase 3 추가
    │         ├───→ GitService     │
    │         └───→ ClaudeService  │
    │                              │
    ├───→ ChatPrWorkflowService    │   ← Phase 3 추가
    │         ├───→ WorkspaceService
    │         ├───→ SessionGitWorkflow ┤
    │         ├───→ GhService      │
    │         ├───→ SessionService │
    │         └───→ SettingsService│
    │                              │
    ├───→ ChatState (직접 참조)    │
    ├───→ UsageService             │
    └───→ AttachmentService        │

SessionList ───→ SessionListDataService          ← Phase 4 추출
                    ├───→ SessionService
                    ├───→ SessionGitWorkflowService
                    ├───→ GitService
                    ├───→ GhService
                    └───→ WorkspaceService
```

### 빠진 것 / 문제점
- **미디에이터 패턴 없음**: 모든 통신이 직접 서비스 호출 + ChatState.OnChange
- **같은 세션을 여러 경로로 로드**: ChatView와 SessionGitWorkflow가 독립적으로 `LoadSessionAsync` 호출. 다른 인스턴스를 가지고 작업할 수 있음

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
                ├─ (첫 메시지면) SessionInitializer.SummarizeAndRenameBranchAsync()
                │     ├─ ClaudeService.SummarizeAsync() → 제목 생성
                │     └─ GitService.RenameBranchAsync() → 브랜치 이름 변경
                │
                ├─ SystemPromptBuilder.BuildAsync()
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

         ※ Phase 4에서 SessionStatusMachine으로 전이 규칙 명시. 무효 전이 시 InvalidOperationException
```

---

# 미해결 구조적 문제 Top 8

| 순위 | 문제 | 영향 | 난이도 |
|------|------|------|--------|
| **1** | **SendMessageAsync 15 파라미터 / ClaudeArgumentBuilder.Build 16 파라미터** — Parameter Object 패턴 필요. `SendMessageOptions` 클래스로 캡슐화 | API 가독성, 유지보수성, 호출부 실수 위험 | 중 |
| **2** | **GitService 648줄 God Object** — cloning, branching, diff 파싱, 캐싱, stash, rebase + git 경로 해석을 단일 클래스에서 담당 | SRP 위반, 테스트 어려움 | 높 |
| **3** | **SessionService 677줄 God Object** — 세션 CRUD, 캐시, 메타데이터, 라이프사이클, 워크트리, 세션 수 제한을 단일 클래스에서 담당 | SRP 위반, 테스트 어려움 | 높 |
| **4** | **ChatMessageOrchestrator 10개 서비스 주입** — 파사드 패턴이나 추가 분해 필요 | 커플링, 테스트 어려움 | 중 |
| **5** | **설정 상수 산재** — 52+개 설정값이 14+개 파일에 `private const`로 분산. `CominomiConstants`/`AppSettings` 미활용 | 변경 추적 불가, 테스트 시 오버라이드 불가 | 중 |
| **6** | **대형 Razor 컴포넌트 잔여** — McpManagerDialog(419줄), AppSettingsContent(396줄). 자식 컴포넌트 추출 가능 | UI 유지보수성 | 중 |
| **7** | **캐시 용량 제한 미비** — `ActivityService._activityCache`, `GitService._branchListCache/GroupCache`에 용량 제한 없음 | 장기 실행 시 메모리 누수 | 낮 |
| **8** | **테스트 커버리지 갭** — ~102개 서비스 중 ~43개 미테스트(57%). 핵심: `ChatMessageOrchestrator`, `ContextService`, `SettingsService`, `ProcessRunner`, StreamEventHandler 6종 | 회귀 방지 불가 | 높 |

### 차기 개선 후보

| 순위 | 문제 | 영향 | 난이도 |
|------|------|------|--------|
| **1** | **ChatState 41개 public 멤버** — 외부에서 직접 접근 가능한 멤버가 과다 | 상태 경계 모호 | 중 |
| **2** | **에러 핸들링 일관성 부재** — 서비스별 에러 처리 패턴 불일치 | 디버깅 어려움 | 중 |
| **3** | **All-Singleton DI** — 45+개 서비스 전부 `AddSingleton`. Scoped/Transient 미사용 | 상태 누수 가능성 | 중 |

---

> 이 문서의 각 섹션을 가리켜 변경 방향을 지시하세요.
