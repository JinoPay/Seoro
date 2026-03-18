# Cominomi 해부 문서 (Anatomy)

> 이 문서는 Cominomi의 모든 시스템을 해부하여 **현재 동작**, **데이터 흐름**, **의존관계**, **빠진 것/문제점**을 기술합니다.
> 각 섹션을 가리켜 "이 부분은 이렇게 변경되어야 한다"고 지휘하는 데 사용하세요.

**코드 규모**: ~174개 소스 파일, ~17,513줄 (Cominomi.Shared에 집중, tests/Cominomi.Shared.Tests 포함)
**프레임워크**: .NET 10.0, MAUI + Blazor, MudBlazor UI
**외부 도구**: Claude CLI (subprocess), Git CLI, GitHub CLI (gh)

### 최근 도입된 구조 개선 (2026-03-18)
| 새 파일 | 역할 |
|---------|------|
| `AtomicFileWriter.cs` | 임시파일→원자적 이동 패턴으로 파일 손상 방지 |
| `StreamEventProcessor.cs` + `IStreamEventProcessor.cs` | ChatView에서 추출한 스트림 이벤트 처리 + 사용량 기록 + 플랜 감지 |
| `ProcessRunner.cs` + `IProcessRunner.cs` | 프로세스 실행 공통 추상화 (ArgumentList, 타임아웃, UTF8) |
| `CominomiConstants.cs` | 중복 문자열 통합 — 기본값(`bypassAll`, `auto`, `squash`), 브랜치 접두사(`cominomi/`), 환경변수(`NO_COLOR`, `GIT_TERMINAL_PROMPT` 등) |

### 구조 개선 Phase 2 (2026-03-18)
| 변경 내용 | 영향 범위 |
|-----------|-----------|
| `ProcessRunner` 전체 마이그레이션 | Git/Shell/Hooks/DependencyCheck/ClaudeCliResolver/McpService → `IProcessRunner` 위임 |
| `ModelDefinitions` 외부화 | `models.json` 파일 로딩 지원, 가격 정보 통합 (`ModelPricing`), 키워드 기반 정규화 |
| `UsageService` 가격 제거 | 하드코딩 딕셔너리 → `ModelDefinitions.GetPricing()` 위임 |
| `AppSettings` 확장 | 요약 모델/프롬프트, 4개 타임아웃 설정 외부화 |
| 중복 상수 통합 | 12개 파일의 매직 스트링 → `CominomiConstants` 참조 |

### 구조 개선 Phase 3 (2026-03-18)
| 변경 내용 | 영향 범위 |
|-----------|-----------|
| `async void` 안티패턴 수정 | 6개 컴포넌트의 이벤트 핸들러 → `void` + `InvokeAsync()` 안전 패턴 |
| `ChatState` 컴포지션 분해 | God Object(366줄) → 파사드(219줄) + `MessageManager` + `StreamingStateManager` + `SettingsStateManager` |
| `SystemPromptBuilder` 추출 | ChatView에서 시스템 프롬프트 빌드 로직 분리 |
| `SessionInitializer` 추출 | ChatView에서 브랜치 로딩 + 첫 메시지 요약/리네임 분리 |
| `ChatPrWorkflowService` 추출 | ChatView에서 PR 생성/병합/충돌해결 워크플로우 분리 |
| `SessionList` 자식 컴포넌트 분리 | `SessionItem.razor` + `SessionListToolbar.razor` 추출 |

### 구조 개선 Phase 4 (2026-03-18)
| 새 파일 / 변경 내용 | 역할 / 영향 범위 |
|---------------------|-----------------|
| `tests/Cominomi.Shared.Tests/` (프로젝트 신규) | xUnit 테스트 인프라 — 84개 테스트 (ContentGrouper, QuestionDetector, ToolDisplayHelper, ExtractToolResultContent, SessionStatusMachine 등) |
| `SessionStatusMachine.cs` (신규, 27줄) | 세션 상태 전이 규칙 정의 + 유효성 검증. `Session.Status`를 `private set`으로 보호 |
| 세션 파일 분리 | `{uuid}.json`(메타데이터) + `{uuid}.messages.json`(메시지) 이중 파일 구조. 이전 단일 파일 형식 자동 마이그레이션 |
| `ToolCall.Output` 절단 | 저장 시 2,000자 초과 도구 출력을 `[truncated, N chars]`로 절단 |
| `SpotlightService` 크래시 복구 | `spotlight-state.json` 영속화 + 앱 시작 시 `RecoverAsync()` 자동 복구 + 동시 Spotlight 가드 + sessionId별 stash 이름 |
| `SessionListDataService.cs` (신규, 228줄) | SessionList에서 캐시/diff stats/merge 상태 체크/정렬/필터 로직 추출. SessionList 634→439줄(31%↓) |

### 구조 개선 Phase 5 (2026-03-18)
| 새 파일 / 변경 내용 | 역할 / 영향 범위 |
|---------------------|-----------------|
| `ModelSelector.razor` (신규, ~90줄) | InputArea에서 모델 선택 UI 추출 — 모델 pill 버튼 + popover + 커스텀 모델 입력 |
| `AttachmentChips.razor` (신규, ~30줄) | InputArea에서 첨부파일 칩 표시 추출 — 순수 표시 컴포넌트 |
| `InputArea.razor` 분해 | 460→~340줄(26%↓). 모델 선택 관련 상태/메서드 5개 + 첨부 칩 마크업을 자식 컴포넌트로 추출 |

### 구조 개선 Phase 7 (2026-03-18) — 차기 개선 후보 #5
| 새 파일 / 변경 내용 | 역할 / 영향 범위 |
|---------------------|-----------------|
| `AppSettingsFactory.cs` (신규) | `IOptionsFactory<AppSettings>` — 설정 파일에서 `AppSettings` 인스턴스 생성 |
| `AppSettingsChangeNotifier.cs` (신규) | `IOptionsChangeTokenSource<AppSettings>` — 설정 저장 시 `IOptionsMonitor` 갱신 트리거 |
| `SettingsService.cs` 변경 | `AppSettingsChangeNotifier` 주입, `SaveAsync` 시 change token 발행 |
| `MauiProgram.cs` 옵션 등록 | `AddOptions<AppSettings>()` + 커스텀 팩토리/체인지 토큰 소스 등록 |
| 서비스 6개 리팩터링 | `ClaudeService`, `WorkspaceService`, `ChatPrWorkflowService`, `SessionService`, `NotificationService`, `PluginService` → `IOptionsMonitor<AppSettings>` 주입 |
| UI 컴포넌트 2개 리팩터링 | `MainLayout.razor`, `SessionList.razor` → `IOptionsMonitor<AppSettings>` 주입 |
| `_Imports.razor` | `@using Microsoft.Extensions.Options` 추가 |

### 구조 개선 Phase 6 (2026-03-18) — Top 10 전항목 해결
| PR | 변경 내용 | 역할 / 영향 범위 |
|----|-----------|-----------------|
| #122 | `GetSessionsAsync` O(n) 최적화 | 인메모리 캐시 + 병렬 I/O + UI 가상화 |
| #123 | Graceful shutdown | `App.xaml.cs` CleanUp() + `IDisposable` 서비스 정리 |
| #124 | MCP 서비스 JSON 직접 읽기 | `~/.claude/mcp.json` + `.claude/mcp.json` 직접 파싱. regex 제거 |
| #125 | Git 워크플로우 자동 리베이스 + PR 닫기 | `RebaseInternalAsync()` + `ClosePrInternalAsync()` |
| #126 | 플러그인 시스템 인프라 | 매니페스트 파싱 + 상태 관리 + enable/disable |
| #127 | 로컬라이제이션 인프라 (.resx + ResourceManager) | `Strings.resx` (ko) + `Strings.en.resx` (en) — 43개 문자열 |
| #128 | `IChatState` 인터페이스 + DI 등록 | ChatState → IChatState 인터페이스 도입, 테스트 가능 |
| #129 | Session 모델 3관심사 분리 | `GitContext` + `PrContext` 서브 객체, `SessionJsonConverter` 하위호환 |
| #130 | ChatView 추가 분해 (899→545줄) | `BranchSelector` + `SessionWorkflowBar` + `LandingPage` 추출 |
| #131 | 중앙 에러 전략 | `AppError` 레코드 + `ErrorCode` enum + 에러 분류(Transient/Permanent) |

### 구조 개선 Phase 6+ (2026-03-18) — 차기 개선 후보 전항목 해결
| PR | 변경 내용 | 역할 / 영향 범위 |
|----|-----------|-----------------|
| #133 | macOS 알림 구현 | `UNUserNotificationCenter` 기반 + 포그라운드 배너 + `NotificationSound` 설정 연동 |
| #134 | 세션 로딩 캐시 | `LoadSessionAsync` 2초 TTL 캐시 — 동일 세션 반복 로드 제거 |
| #135 | MainLayout 분해 | `IThemeService` + `SidebarToolbar` + `MainToolbar` 추출. 238→~127줄(47%↓) |
| #136 | 옵션 패턴 도입 | `IOptionsMonitor<AppSettings>` + `AppSettingsFactory` + `AppSettingsChangeNotifier`. 8개 서비스/컴포넌트 전환 |
| #137 | 플러그인 실행 엔진 | EntryPoint 로딩/실행/샌드박싱 + hooks·skills 매니페스트 자동 등록 |

### 구조 개선 Phase 8 (2026-03-18) — 신규 구조적 문제 #2 해결
| 변경 내용 | 역할 / 영향 범위 |
|-----------|-----------------|
| `AtomicFileWriter.AppendAsync()` 추가 | 기존 파일 내용을 읽고 append 후 원자적 쓰기. .gitignore, JSONL 등 append 패턴 지원 |
| `SpotlightService` 원자적 쓰기 | `PersistStateAsync()`의 `File.WriteAllTextAsync` → `AtomicFileWriter.WriteAsync` |
| `ContextService` 원자적 쓰기 | notes/todos/plans 저장 + .gitignore append 총 6곳 → `AtomicFileWriter` 전환 |
| `AttachmentService` 원자적 쓰기 + async 전환 | `EnsureGitignore` sync → `EnsureGitignoreAsync` + `AtomicFileWriter` 전환 |
| `UsageService` 원자적 쓰기 | JSONL append → `AtomicFileWriter.AppendAsync` 전환 |

### 구조 개선 Phase 7 (2026-03-18) — 차기 개선 후보 #3 해결
| 변경 내용 | 역할 / 영향 범위 |
|-----------|-----------------|
| `IThemeService` + `ThemeService` (신규) | MudTheme 정의 + 다크/라이트 모드 토글 + 설정 연동을 MainLayout에서 서비스로 추출 |
| `SidebarToolbar.razor` (신규, ~40줄) | 사이드바 브랜드 + 테마/활동/사용량/설정 버튼을 MainLayout에서 추출 |
| `MainToolbar.razor` (신규, ~55줄) | 메인 툴바 (사이드바 토글 + 제목/브랜치 + 상태바 + 패널 토글)를 MainLayout에서 추출 |
| `MainLayout.razor` 분해 | 238→~127줄(47%↓). 3패널 셸 + MudThemeProvider + 의존성 체크만 남음 |

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
- [해결된 구조적 문제](#해결된-구조적-문제-phase-15)
- [구조적 문제 Top 10 (전항목 해결)](#구조적-문제-top-10--전항목-해결-완료-)

---

# Part I: 기반 레이어

## 1. 앱 부트스트랩 & DI

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `src/Cominomi/MauiProgram.cs` | 141 | DI 컨테이너 전체 설정 + Spotlight 복구 + 옵션 패턴 |
| `src/Cominomi/App.xaml.cs` | 22 | MAUI App 수명주기 |
| `src/Cominomi/MainPage.xaml` | 9 | BlazorWebView 호스트 |
| `src/Cominomi/Components/Routes.razor` | - | Blazor 라우팅 |

### 현재 동작
`MauiProgram.cs:14-117`에서 앱이 부트스트랩됨:

1. **Serilog 초기화** (`:16-33`): 콘솔 + 롤링 파일 로그 (14일 보관)
2. **MAUI 설정** (`:35-41`): BlazorWebView + OpenSans 폰트
3. **MudBlazor** (`:50-59`): Snackbar 설정 (하단 우측, 3초, 최대 3개)
4. **서비스 등록** (`:63-92`): **35개 전부 Singleton** (옵션 패턴 헬퍼 포함)
5. **Spotlight 크래시 복구** (`:104-113`): 앱 시작 시 `ISpotlightService.RecoverAsync()` 호출 — 이전 실행에서 비정상 종료된 Spotlight 상태 자동 복원

```
등록 순서 (MauiProgram.cs:63-91):
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
  IUsageService         → UsageService
  IMcpService           → McpService
  INotificationService  → NotificationService (플랫폼별)
  IActivityService      → ActivityService
  IStreamEventProcessor → StreamEventProcessor
  IProcessRunner        → ProcessRunner
  ISystemPromptBuilder  → SystemPromptBuilder          ← Phase 3 추가
  ISessionInitializer   → SessionInitializer           ← Phase 3 추가
  IChatPrWorkflowService → ChatPrWorkflowService       ← Phase 3 추가
  SessionListDataService → SessionListDataService      ← Phase 4 추가
```

`MainPage.xaml`은 `<BlazorWebView>`를 호스트하며, `Routes.razor`가 `MainLayout`으로 라우팅.

### 빠진 것 / 문제점
- **모든 서비스가 Singleton**: `ChatState`는 가변 `ConcurrentDictionary`를 가지고, `SkillRegistry`는 가변 `List`를 가짐. 스레드 안전성이 관례에만 의존
- ~~**옵션 패턴 미사용**: 각 서비스가 `ISettingsService.LoadAsync()`를 직접 호출하여 설정을 읽음. `IOptions<T>` 패턴으로 중앙화 가능~~ → ✅ **해결**: `IOptionsMonitor<AppSettings>` 도입. `AppSettingsFactory` + `AppSettingsChangeNotifier`로 설정 파일 로딩/갱신 자동화. 서비스 6개 + UI 2개 마이그레이션 완료
- **서비스 건강 체크 없음**: Git/gh/Claude CLI가 없어도 앱이 시작됨 (런타임에만 실패). 다만 Spotlight 복구는 실패 시 로그 경고 후 계속 진행
- ~~**Graceful shutdown 없음**: 스트리밍 중인 Claude 프로세스가 앱 종료 시 고아 프로세스가 될 수 있음~~ → ✅ **해결**: `App.xaml.cs` CleanUp()에서 `IClaudeService`, `ISpotlightService`, `ChatState`, `SessionListDataService` Dispose + `Log.CloseAndFlush()`
- ~~**ChatState에 인터페이스 없음** (`:64`): 직접 클래스로 등록되어 테스트 불가~~ → ✅ **해결**: `IChatState` 인터페이스 도입 + `builder.Services.AddSingleton<IChatState, ChatState>()` DI 등록

---

## 2. 영속화 레이어 (JSON 파일 스토리지)

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/AppPaths.cs` | 23 | 디렉토리 경로 정의 |
| `Shared/Services/JsonDefaults.cs` | 12 | 공유 직렬화 옵션 |
| `Shared/Services/SettingsService.cs` | 38 | settings.json 캐시 |
| `Shared/Services/UsageService.cs` | 196 | usage.jsonl (별도 위치!) |

### 현재 동작

**디렉토리 구조** (`AppPaths.cs:5-16`):
```
%APPDATA%/Cominomi/
├── settings.json          ← SettingsService (인메모리 캐시)
├── hooks.json             ← HooksEngine
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
└── archived-contexts/     ← SessionService (아카이브)
    └── {workspaceName}/{sessionName}/.context/
```

**별도 위치**: `UsageService`는 `Environment.SpecialFolder.LocalApplicationData`에 `usage.jsonl`을 저장 (다른 데이터와 불일치).

**읽기 패턴**: 모든 서비스가 `Directory.GetFiles(dir, "*.json")` → 파일마다 `File.ReadAllTextAsync` → `JsonSerializer.Deserialize` → 실패 시 로그 경고 후 건너뜀.

**쓰기 패턴**: `JsonSerializer.Serialize` → `File.WriteAllTextAsync`로 전체 파일 덮어쓰기. 원자적 쓰기가 아님.

**세션 파일 구조** (Phase 4에서 분리):
- `SaveSessionAsync()`: 메타데이터(`{uuid}.json`)와 메시지(`{uuid}.messages.json`)를 별도 파일로 저장. `ToolCall.Output`이 2,000자 초과 시 `[truncated, N chars]`로 절단
- `GetSessionsAsync()`: 메타데이터 파일만 읽어 경량 목록 생성 (`.messages.json` 파일 제외)
- `LoadSessionAsync()`: 메타데이터 로드 후 `.messages.json`에서 메시지 별도 로드 + `MigrateToParts()` 호출. 이전 단일 파일 형식도 자동 호환 (메시지가 인라인에 있으면 그대로 사용)

### 빠진 것 / 문제점
- ~~**파일 락 없음**: 병렬 세션이 동시에 `SaveSessionAsync()`를 호출하면 데이터 손상 가능~~ → ✅ **해결**: `AtomicFileWriter` (임시파일→원자적 이동) + per-session `SemaphoreSlim` 락 도입. 전체 서비스(Session/Workspace/Settings/Task/Memory/Hooks/Skill)에 적용.
- ~~**세션 파일 무한 성장**: 100턴 대화 + 도구 호출 결과가 하나의 JSON 파일에 전부 포함. 수 MB까지 성장 가능~~ → ✅ **해결**: Phase 4에서 메타데이터/메시지 분리 + 도구 출력 2,000자 절단. 목록 로드 시 메시지 파일 불필요
- ~~**인덱싱 없음**: `GetSessionsAsync()`가 모든 메타데이터 파일을 역직렬화 — O(n) 성능. 세션 수 증가 시 사이드바 로딩 느려짐~~ → ✅ **해결**: `ConcurrentDictionary` 인메모리 캐시 + `EnsureCacheLoadedAsync()` 1회 로드 + 병렬 파일 읽기
- **백업/마이그레이션 없음**: JSON 스키마 변경 시 기존 파일이 역직렬화 실패 → 데이터 손실
- **Usage 위치 불일치**: `AppData/Roaming`과 `AppData/Local`에 분산 저장
- ~~**GetSessionsAsync 수동 매핑** (`SessionService.cs:46-68`): 15개 프로퍼티를 수동 복사. DTO나 프로젝션이 없음~~ → ✅ **해결**: Phase 4에서 메타데이터 파일을 직접 역직렬화. 수동 프로퍼티 복사 제거

---

## 3. 데이터 모델

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Models/Session.cs` | 78 | 핵심 엔티티 + 상태 전이 검증, `Git`/`Pr` 서브 객체로 관심사 분리 |
| `Shared/Models/GitContext.cs` | 15 | Git worktree/branch 관련 속성 그룹 |
| `Shared/Models/PrContext.cs` | 15 | PR/이슈 관련 속성 그룹 |
| `Shared/Models/SessionJsonConverter.cs` | 205 | 기존 플랫 JSON ↔ 신규 중첩 JSON 하위호환 컨버터 |
| `Shared/Models/Workspace.cs` | 42 | 리포 설정 |
| `Shared/Models/ChatMessage.cs` | 53 | 메시지 + Parts |
| `Shared/Models/StreamEvent.cs` | 165 | Claude CLI 스트림 프로토콜 |
| `Shared/Models/ToolCall.cs` | 11 | 도구 호출 기록 |
| `Shared/Models/AppSettings.cs` | 35 | 앱 설정 + 요약 모델/프롬프트 + 타임아웃 설정 |
| `Shared/CominomiConstants.cs` | 45 | 공유 상수 (기본값, 환경변수, 브랜치 접두사) |

### Session (핵심 엔티티)

`Session.cs` — 3가지 관심사를 `GitContext`/`PrContext` 서브 객체로 분리:

```
[대화 관심사 — Session 루트]
  Id, Title, Messages, Model, PermissionMode, EffortLevel, AgentType
  ConversationId, MaxTurns, MaxBudgetUsd
  TotalInputTokens, TotalOutputTokens, WorkspaceId
  Status, ErrorMessage, PlanCompleted, PlanFilePath

[Git 관심사 — session.Git (GitContext)]
  WorktreePath, BranchName, BaseBranch, IsLocalDir, AdditionalDirs

[PR/이슈 관심사 — session.Pr (PrContext)]
  PrUrl, PrNumber, IssueNumber, IssueUrl, ConflictFiles
```

`SessionJsonConverter`가 기존 플랫 포맷(v1)과 중첩 포맷(v2) 모두 역직렬화 지원.

### 상태 머신 (SessionStatus) — Phase 4에서 검증 추가

`Session.cs:7-17` — 9가지 상태. Phase 4에서 `SessionStatusMachine`(27줄)으로 전이 규칙 명시 + `Session.Status`를 `private set`으로 보호:

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
public List<ToolCall> ToolCalls { get; set; } // 레거시: 도구 호출 목록
public List<ContentPart> Parts { get; set; }  // 신규: 인터리브 렌더링
```

`MigrateToParts()` (`:36-45`): 매 로드마다 호출. Parts가 비어있으면 ToolCalls→Parts, Text→Parts로 변환.

### 빠진 것 / 문제점
- ~~**상태 전이 검증 없음**: 아무 상태에서 아무 상태로 전환 가능~~ → ✅ **해결**: Phase 4에서 `SessionStatusMachine` + `Session.TransitionStatus()` 도입. `Status`는 `private set`으로 보호. 11+ 사이트에서 직접 할당 제거
- **기타 검증 없음**: 빈 `WorkspaceId`로 Session 생성 가능. 음수 토큰 카운트 가능
- **Session이 너무 큼**: 대화 + Git + PR을 하나에 담아서, 하나를 변경하면 전체를 다시 직렬화
- **Parts 이중 저장**: `Text`와 `Parts[].Text`가 동시에 존재. `AppendText()`가 양쪽 다 업데이트 (`ChatState.cs:184-202`)
- **모든 모델이 가변**: `public set`만 있고, 불변(immutable) 보장 없음. 어디서든 조용히 수정 가능
- ~~**ModelDefinitions 하드코딩**: opus/sonnet/haiku 3개 모델만. 새 모델 추가는 코드 변경 필요~~ → ✅ **해결**: `ModelDefinitions`가 `models.json` 외부 설정 파일 로딩 지원. 가격 정보(`ModelPricing`) 통합. 키워드 기반 정규화. 기본 내장값 폴백. **참고**: `models.json`은 리포에 포함되지 않음 (선택적). `MauiProgram.cs:91-92`에서 `AppPaths.Settings/models.json` 경로를 로드하며, 파일 없으면 내장 기본값(Opus/Sonnet/Haiku) 사용
- **UI 전용 모델이 도메인과 혼재**: `ContentGroup`, `ActivitySummaryInfo` 등이 `Models/` 네임스페이스에 있음

---

# Part II: 외부 도구 통합

## 4. 셸 서비스 & 프로세스 실행

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/ShellService.cs` | 173 | 셸 감지, WhichAsync |
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

### 빠진 것 / 문제점
- ~~**통합 프로세스 실행 추상화 없음**~~ → ✅ **해결**: 7개 서비스가 `IProcessRunner`로 마이그레이션 완료
- **셸 감지 1회 캐싱**: 앱 실행 후 Git 설치하면 재시작 필요
- **WhichAsync 3초 고정 타임아웃**: 에러 전파 없음, 실패 시 null 반환
- ~~**GitService/GhService는 ShellService를 사용하지 않음**: `"git"`, `"gh"`를 직접 하드코딩~~ → `IProcessRunner`로 통합되어 환경변수/타임아웃 일관성 확보

---

## 5. Git 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/GitService.cs` | 447 | 모든 git 명령 (`IProcessRunner` 기반) |
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

**프로세스 실행** (`RunGitAsync()`, `:186-197`):
`IProcessRunner.RunAsync()`를 통해 `git` 명령 실행. 인수는 배열(`params string[]`)로 전달하며 `ArgumentList`를 사용하여 셸 해석 없이 안전하게 전달. 환경변수는 `CominomiConstants.Env.GitEnv`로 통합.

**예외**: `CloneAsync()`는 stderr 진행률 실시간 보고가 필요하여 `CreateStreamingGitProcess()`로 직접 `Process` 사용.

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
- ~~**타임아웃 없음**~~ → ✅ **해결**: `IProcessRunner` 기본 타임아웃 30초 적용
- **stdout 전체 메모리 로드**: 대형 diff 출력 시 메모리 문제
- **ParseDiff 취약**: `header.LastIndexOf(" b/")`로 파일 경로 추출 — 경로에 공백이나 ` b/`가 포함되면 실패
- **캐싱 없음**: `DetectDefaultBranchAsync`, `ListBranchesAsync`가 매번 git 프로세스 생성
- **`git` 하드코딩**: PATH에 git이 없으면 실패. 설정으로 경로 지정 불가
- **git clone → push 사이에 fetch 없음**: 다른 사람이 base branch에 푸시한 변경사항 반영 안 됨

---

## 6. GitHub CLI 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/GhService.cs` | 145 | PR/이슈 CRUD |
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
- ~~**커맨드 인젝션 위험**: title/body 이스케이핑이 `Replace("\"", "\\\"")`만~~ → ✅ **해결**: `ProcessStartInfo.ArgumentList` 방식으로 전환하여 OS가 인자 이스케이핑 처리. `IProcessRunner`를 통한 통합 프로세스 실행.
- **페이지네이션 없음** (`:75`): `--limit 30` 하드코딩. 31번째 이슈부터 안 보임
- **PR 병합 시 체크 대기 없음**: CI가 돌고 있어도 즉시 병합 시도
- **IsAuthenticatedAsync의 workingDir가 "."** (`:59`): 현재 디렉토리가 git 리포가 아닐 수 있음
- ~~**`gh` 하드코딩**~~ → IProcessRunner를 통해 실행하나, 아직 PATH 의존
- **rate limiting 인식 없음**: GitHub API 제한에 도달해도 재시도 없음

---

## 7. Claude CLI 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/ClaudeService.cs` | 374 | CLI 프로세스 관리 + 스트리밍 |
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
- ~~**시스템 프롬프트 이스케이핑 취약**~~ → ✅ **해결**: `\r\n`, `\n`, `\r`, `\t`, `\0` 제어 문자 이스케이핑 추가
- **재시도 로직 60줄 복붙** (`:106-148`): `--verbose` 재시도가 스트리밍 루프 전체를 복사
- **AgentProcess.Cancel()에서 Kill(entireProcessTree: true)** (`:367`): 일부 플랫폼에서 고아 프로세스 남을 수 있음
- **stderr 수집 태스크 실패 무시** (`:102`): `try { await stderrTask; } catch { }` — 에러 정보 손실

---

# Part III: 워크플로우 오케스트레이션

## 8. 세션 생명주기

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/SessionService.cs` | 468 | 세션 CRUD + 워크트리 + 파일 분리 저장 |
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

### 빠진 것 / 문제점
- **한국어 제목 → 빈 슬러그**: `[^a-z0-9\-]` 정규식 (`:396`)이 한글을 전부 제거. 결과: `cominomi/20260318-143022` (타임스탬프 폴백)
- **세션 수 제한 없음**: 워크스페이스당 수백 개 워크트리 생성 가능. git 성능 저하
- ~~**상태 전이 검증 없음**: Archived에서 Ready로 직접 변경해도 에러 없음~~ → ✅ **해결**: Phase 4에서 `SessionStatusMachine` 도입. 무효 전이 시 `InvalidOperationException`. `session.Status = X` 직접 할당 전부 `session.TransitionStatus(X)`로 교체
- ~~**GetSessionsAsync O(n)** (`:31-78`): 파일 수 만큼 직렬화. 페이지네이션 없음~~ → ✅ **해결**: `ConcurrentDictionary` 인메모리 캐시 + 1회 로드 + 병렬 I/O
- ~~**워크트리 초기화 레이스 컨디션**: Pending 세션에 메시지 전송 시 `InitializeWorktreeAsync`가 호출되는데, 빠르게 두 번 전송하면 워크트리 이중 생성 시도 가능~~ → ✅ **해결**: `SessionService`에 per-session `SemaphoreSlim` (`_worktreeInitLocks`) 추가하여 동시 호출 직렬화 + 락 획득 후 상태 재확인(Pending이 아니면 즉시 반환). `ChatView`에서도 `_isInitializingWorktree` 플래그로 UI-level 이중 진입 방지
- **CityName 아카이브 경로**: `CityNames.GetRandom()` (46개 도시)으로 이름 생성, 하지만 파일 경로에 부적합한 문자 없음을 보장하지 않음

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

### 빠진 것 / 문제점
- ~~**PR 생성 경로 2개**~~ → ✅ **해결**: Phase 3에서 `IChatPrWorkflowService`로 통합
- ~~**자동 리베이스 없음**~~ → ✅ **해결**: `ConflictDetected` 시 `RebaseInternalAsync()` 자동 호출 → fetch → rebase → force-push → 재시도 병합 (3단계 복구)
- ~~**같은 세션 3~4번 로드**~~ → ✅ **해결**: (1) `MergeAllAsync`는 Internal 메서드로 1회만 로드, (2) `SessionService.LoadSessionAsync`에 2초 TTL 캐시 추가 — `SaveSessionAsync` 시 캐시 갱신, `Delete`/`Cleanup` 시 무효화
- ~~**롤백 없음**: PR 생성 성공 후 병합 실패 시, PR이 열린 채로 방치~~ → ✅ **해결**: `ClosePrInternalAsync()` 도입. 병합 실패 시 PR 자동 닫기
- **강제 푸시에 확인 없음**: `ForcePushAndMerge`가 UI에서 직접 호출
- **충돌 감지 오탐 가능**: `"merge"` 단어가 에러와 무관한 맥락에 나올 수 있음

---

## 10. ChatView — 중앙 오케스트레이터

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Components/Chat/ChatView.razor` | ~545 | 오케스트레이터 (Phase 3+6에서 994→899→545줄 감소) |
| `Shared/Components/Chat/BranchSelector.razor` | ~106 | 브랜치 선택 UI (Phase 6 추출) |
| `Shared/Components/Chat/SessionWorkflowBar.razor` | ~205 | PR/워크플로우 바 (Phase 6 추출) |
| `Shared/Components/Chat/LandingPage.razor` | ~93 | 랜딩 페이지 (Phase 6 추출) |

### 현재 동작

**주입 서비스 13개** (Phase 3에서 18→14, Phase 6에서 14→13개 축소):
```
IChatState, ClaudeService, SessionService, AttachmentService,
HooksEngine, JSRuntime, Logger, Snackbar,
NotificationService, StreamEventProcessor,
SystemPromptBuilder, SessionInitializer, ChatPrWorkflowService
```
> Phase 3 제거: ContextService, MemoryService, IGhService, IGitService, IWorkspaceService, ISettingsService, ISessionGitWorkflowService
> Phase 6 제거: UsageService (StreamEventProcessor로 위임)
> → `SystemPromptBuilder`, `SessionInitializer`, `ChatPrWorkflowService`로 위임

**이 컴포넌트가 담당하는 것들**:

| 기능 | 줄 범위 (대략) | 설명 |
|------|----------------|------|
| 랜딩 페이지 | (추출됨) | `LandingPage.razor` — 세션 없을 때 워크스페이스 생성 + 최근 대화 |
| 브랜치 선택 | (추출됨) | `BranchSelector.razor` — Pending 세션의 브랜치 피커 (검색/필터) |
| 워크플로우 바 | (추출됨) | `SessionWorkflowBar.razor` — PR 생성/병합/충돌 해결/강제 푸시 버튼 |
| 메시지 렌더링 | 마크업 영역 | MessageBubble 루프 + 스트리밍 인디케이터 |
| 입력 영역 | 마크업 영역 | InputArea 래핑 + 이벤트 핸들링 |
| 메시지 전송 | `HandleSend` | 워크트리 초기화 + 첨부파일 + 스킬 확장 |
| 스트림 처리 | `ProcessMessageAsync` | `StreamEventProcessor`에 위임 (1줄 호출) |
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

### 빠진 것 / 문제점
- ~~**God Component (1,461줄, 18개 서비스)**~~ → ✅ **해결**: Phase 1에서 `StreamEventProcessor`(516줄) 추출, Phase 3에서 `SystemPromptBuilder`, `SessionInitializer`, `ChatPrWorkflowService` 추출, Phase 6에서 `BranchSelector`(106줄), `SessionWorkflowBar`(205줄), `LandingPage`(93줄) 추출. **1,461→994→899→545줄, 18→14→13개 서비스**로 63% 감소.
- ~~**스트림 처리 300줄 switch**~~ → ✅ **해결**: `StreamEventProcessor` 서비스로 분리
- ~~**사용량 추적 4단계 폴백**~~ → ✅ **해결**: `StreamEventProcessor.FinalizeAsync()`로 캡슐화
- ~~**플랜 모드 3계층 감지**~~ → ✅ **해결**: `StreamEventProcessor.FinalizeAsync()`로 캡슐화
- **ProcessMessageAsync가 Task.Run에서 실행**: 백그라운드 스레드에서 `InvokeAsync(StateHasChanged)` 호출. 동작하지만 예외 미관찰 위험
- ~~**async void HandleStateChanged**~~ → ✅ **해결**: Phase 3에서 `void` + `InvokeAsync()` try-catch 패턴으로 변환
- ~~**CreatePr AI vs 직접 경로 2개**~~ → ✅ **해결**: Phase 3에서 PR 워크플로우를 `IChatPrWorkflowService`로 통합. ChatView는 UI 피드백만 담당

---

## 10.5 스트림 이벤트 프로세서

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/StreamEventProcessor.cs` | 516 | ChatView에서 추출한 스트림 이벤트 처리 핵심 |
| `Shared/Services/IStreamEventProcessor.cs` | 47 | 인터페이스 + `StreamProcessingContext` DTO |

### 현재 동작

ChatView의 `ProcessMessageAsync`에서 추출된 핵심 로직. 단일 진입점 `ProcessEventAsync(StreamEvent, StreamProcessingContext)`로 12종 스트림 이벤트를 처리:

**처리 항목**:
- **이벤트 디스패치**: type/subtype 기반 switch → ChatState 메서드 호출 (AddToolCall, AppendText, AppendThinking 등)
- **도구 결과 매칭**: `toolResultBlockMap`으로 tool_use→tool_result 매핑
- **토큰 누적**: message_start/message_delta에서 입출력 토큰 캡처
- **중간 저장**: content_block_stop마다 `SessionService.SaveSessionAsync()`

**FinalizeAsync** — 스트림 종료 후 3가지 후처리:
1. **사용량 기록** (3단계 폴백): result 이벤트 → 누적 토큰 → 비용 전용 → 후처리 계산
2. **플랜 감지** (3계층): tool_use 기반 → 텍스트 패턴 검색 → 파일 존재 감지
3. **질문 감지**: `QuestionDetector` → `QuickResponseBar` 제안

### 빠진 것 / 문제점
- **ChatState 직접 변경**: `ProcessEventAsync` 내에서 `ChatState`의 가변 상태를 직접 수정. 부수효과 추적 어려움
- **`StreamProcessingContext` 가변 DTO**: 참조 타입으로 컨텍스트 상태 공유. 호출자와 프로세서가 같은 객체를 변경

---

# Part IV: UI 컴포넌트 시스템

## 11. 레이아웃 아키텍처

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Components/Layout/MainLayout.razor` | ~127 | 3패널 레이아웃 셸 (테마/의존성 체크 위임) |
| `Shared/Components/Layout/SidebarToolbar.razor` | ~40 | 사이드바 브랜드 + 테마/활동/설정 버튼 |
| `Shared/Components/Layout/MainToolbar.razor` | ~55 | 메인 툴바 (제목/브랜치/상태바/패널 토글) |
| `Shared/Components/Layout/DetailPanel.razor` | ~45 | 우측 패널 |
| `Shared/Components/Layout/MainTabBar.razor` | ~29 | 탭 바 |
| `Shared/Components/Layout/StatusBar.razor` | ~21 | 상태 바 |
| `Shared/Services/IThemeService.cs` | ~13 | 테마 상태 인터페이스 |
| `Shared/Services/ThemeService.cs` | ~70 | 테마 상태 관리 (다크/라이트 모드 + MudTheme + 설정 연동) |
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

**MainLayout 책임 분리** (Phase 7):
- ~~테마 관리~~ → `IThemeService`/`ThemeService`로 추출 (MudTheme 정의 + 다크/라이트 토글 + 설정 연동)
- ~~사이드바 툴바~~ → `SidebarToolbar.razor`로 추출 (브랜드 + 테마/활동/사용량/설정 버튼)
- ~~메인 툴바~~ → `MainToolbar.razor`로 추출 (사이드바 토글 + 제목/브랜치 + 상태바 + 패널 토글)
- MainLayout에 남은 책임: 3패널 셸 구성, MudThemeProvider 바인딩, 의존성 체크 → SetupDialog, UsageDashboard 다이얼로그

**TabManager** (`TabManager.cs`):
- 타입: Chat, FileContent, FileDiff, Activity
- Chat 탭은 항상 존재. 다른 탭은 동적 추가/제거
- `OnTabChanged` 이벤트

### CSS 디자인 토큰 시스템

`src/Cominomi/wwwroot/app.css` (3,169줄)에 정의된 토큰 시스템:

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
| `Chat/` | 13 | ChatView, InputArea, MessageBubble, LinkIssueDialog, ActivitySummary, PlanReviewBar, QuickResponseBar, StreamingIndicator, **ModelSelector** (Phase 5), **AttachmentChips** (Phase 5), **BranchSelector** (Phase 6), **SessionWorkflowBar** (Phase 6), **LandingPage** (Phase 6) |
| `Diff/` | 1 | DiffPanel |
| `Files/` | 2 | FileContentView, FileDiffView |
| `Layout/` | 6 | MainLayout, **SidebarToolbar** (Phase 7), **MainToolbar** (Phase 7), DetailPanel, MainTabBar, StatusBar |
| `Settings/` | 6 | SettingsPage, AppSettingsContent, WorkspaceSettingsContent, McpManagerDialog, SlashCommandsEditor, UsageDashboard |
| `Setup/` | 1 | SetupDialog |
| `Sidebar/` | 6 | SessionList, SessionItem, SessionListToolbar, CreateWorkspaceDialog, SidebarExplorer, SidebarChanges |
| `Spotlight/` | 1 | SpotlightToggle |
| `Tools/` | 7 | ToolCallCard, ToolGroupCard, BashToolWidget, EditToolWidget, GlobToolWidget, GrepToolWidget, ReadToolWidget |
| 기타 | 3 | _Imports, Home, Routes |
| **합계** | **45** | |

### 빠진 것 / 문제점
- **MainLayout이 너무 많은 책임**: 레이아웃 + 테마 + 의존성 체크 + 다이얼로그 관리
- **패널 크기 조절 불가**: CSS 고정 너비. 사용자가 사이드바나 디테일 패널 크기를 조절할 수 없음
- **키보드 네비게이션 없음**: 패널 간 이동에 접근성 지원 없음
- **탭 메모리에 파일 콘텐츠 보관**: `MainTab.FileContent`가 문자열로 메모리에 상주. 퇴출 정책 없음
- **디자인 토큰 파일 미분리**: 토큰이 `app.css`(3,169줄)에 컴포넌트 스타일과 혼재. 별도 `tokens.css` 없음

---

## 12. 사이드바 & 세션 관리 UI

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Components/Sidebar/SessionList.razor` | ~439 | 세션 목록 (Phase 3: 719→634줄, Phase 4: 634→439줄) |
| `Shared/Components/Sidebar/SessionItem.razor` | ~91 | 세션 행 렌더링 (Phase 3 추출) |
| `Shared/Components/Sidebar/SessionListToolbar.razor` | ~34 | 툴바 + 필터 (Phase 3 추출) |
| `Shared/Services/SessionListDataService.cs` | 228 | 캐시/정렬/필터/diff stats/merge 상태 체크 (Phase 4 추출) |
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
- ~~**SessionList 719줄 God Component**~~ → ✅ **해결**: Phase 3에서 `SessionItem.razor`+`SessionListToolbar.razor` 자식 컴포넌트 추출(719→634줄). Phase 4에서 `SessionListDataService`로 데이터/캐시/필터 로직 추출(634→439줄). 총 39%↓
- **가상화 없음**: 세션이 많아지면 전체 DOM에 렌더링
- ~~**세션 선택 시 전체 로드**: `LoadSessionAsync()`가 전체 메시지 포함 파일을 동기적으로 읽음. 대형 세션은 UI 버벅임~~ → ✅ **해결**: `LoadSessionAsync` 2초 TTL 캐시 도입 (#134). 동일 세션 재로드 시 캐시 히트
- **FileSystemWatcher 플러딩**: Windows에서 빠른 파일 변경 시 이벤트 폭주 → UI 업데이트 과다

---

## 13. 채팅 컴포넌트

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Components/Chat/MessageBubble.razor` | ~286 | 메시지 렌더링 |
| `Shared/Components/Chat/InputArea.razor` | ~344 | 입력 영역 (Phase 5에서 459→344줄) |
| `Shared/Components/Chat/StreamingIndicator.razor` | ~122 | 스트리밍 상태 |
| `Shared/Components/Chat/PlanReviewBar.razor` | ~128 | 플랜 리뷰 |
| `Shared/Components/Chat/QuickResponseBar.razor` | ~19 | 빠른 응답 |
| `Shared/Components/Chat/LinkIssueDialog.razor` | 209 | GitHub 이슈 연결 다이얼로그 |
| `Shared/Components/Chat/ActivitySummary.razor` | 141 | 도구 활동 요약 (접기/펴기) |
| `Shared/Components/Tools/ToolCallCard.razor` | ~130 | 도구 호출 카드 |
| `Shared/Components/Tools/ToolGroupCard.razor` | 92 | 연속 도구 호출 그룹 카드 |
| `Shared/Services/ContentGrouper.cs` | ~244 | 파트 그룹화 |
| `Shared/Services/QuestionDetector.cs` | ~96 | 질문 감지 |
| `Shared/Services/ToolDisplayHelper.cs` | 217 | 도구 결과 UI 라벨 (헤더/요약/설명) |

### 현재 동작

**MessageBubble**: ContentGrouper로 Parts를 그룹화:
- ToolGroup: 연속된 도구 호출을 접기
- Thinking: 사고 블록 접기
- Text: 최종 텍스트와 중간 텍스트 구분

**InputArea** (~340줄 — Phase 5에서 축소):
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

**ToolDisplayHelper** (217줄 — 정적 유틸리티):
- `GetHeaderLabel()`: 도구 입력 JSON에서 컨텍스트 추출 → "Read MessageBubble.razor" 같은 라벨 생성
- `GetCompactResult()`: 출력에서 "50줄 읽음", "3개 파일 일치" 같은 힌트 생성
- `BuildDescriptiveSummary()`: 도구 호출의 서술형 요약
- Read/Write/Edit/Grep/Glob/Bash/Agent/WebFetch/WebSearch/NotebookEdit/TodoWrite 처리
- ~~**한국어 문자열 하드코딩**~~ → ✅ **해결**: `Strings.*` 접근자로 전환 ("줄 읽음" → `Strings.Tool_ReadHint(lineCount)` 등)

**LinkIssueDialog** (209줄): GitHub 이슈를 세션에 연결하는 모달. 이슈 목록 검색/필터 + 새 이슈 생성

**ActivitySummary** (141줄): 도구 활동 요약 블록. 사고(thinking) 블록 접기/펴기, 도구 호출 카운트 표시

### 빠진 것 / 문제점
- **"중간 텍스트" 휴리스틱** (`ContentGrouper.cs`): 하드코딩된 한국어/영어 패턴으로 텍스트를 "중간"으로 분류. 오분류 가능
- ~~**InputArea 459줄**: 텍스트 입력 + 파일 첨부 + 모델/권한/노력 선택이 한 컴포넌트에~~ → ✅ **해결**: Phase 5에서 `ModelSelector.razor`+`AttachmentChips.razor` 자식 컴포넌트 추출. 460→~340줄(26%↓)
- **도구 입력 JSON 매 렌더 파싱**: ToolCallCard가 `JsonSerializer.Deserialize`를 매 렌더마다 수행. 캐싱 없음
- **QuestionDetector 제한적**: 마지막 문장이 `?`로 끝나야 질문으로 감지. 질문이 본문 중간에 있으면 놓침

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
- **스킬 체이닝 불가**: `/commit` 후 `/review` 자동 실행 같은 워크플로우 불가
- **프로젝트 경로 의존**: 워크스페이스 변경 시에만 커스텀 스킬 리로드

---

## 15.5 플러그인 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/PluginService.cs` | ~290 | 플러그인 탐색 + 매니페스트 파싱 + 로드/언로드 위임 |
| `Shared/Services/PluginExecutionEngine.cs` | ~355 | 플러그인 실행 엔진: 로드/언로드/실행 + hooks·skills 등록 |

### 현재 동작
`~/.claude/plugins/` 디렉토리 하위의 각 폴더에서 `manifest.json`을 읽어 플러그인 목록 획득. DI에 `IPluginService` + `IPluginExecutionEngine`으로 등록. `PluginStatus` enum으로 상태 관리 (Discovered, Valid, Invalid, Loaded, Error). 설정에서 enable/disable 가능.

**플러그인 실행 엔진**: 앱 시작 시 활성+유효 플러그인 자동 로드. EntryPoint 파일을 확장자별 인터프리터(`node`, `python3`, `bash` 등)로 자식 프로세스 실행. 프로세스 격리 + 30초 타임아웃으로 샌드박싱. 매니페스트에 `hooks`/`skills` 배열을 선언하면 HooksEngine·SkillRegistry에 자동 등록. 언로드 시 등록 해제.

### 빠진 것 / 문제점
- **디렉토리 없으면 무시**: 사용자에게 플러그인 기능 존재를 알리지 않음
- **stdin/stdout 프로토콜 미정의**: 플러그인 ↔ 앱 간 구조화된 데이터 교환 방식 없음 (현재 환경변수만 전달)

---

## 16. MCP 서비스

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/McpService.cs` | 197 | MCP 서버 관리 |
| `Shared/Models/McpServer.cs` | ~21 | 서버 모델 |
| `Shared/Components/Settings/McpManagerDialog.razor` | ~342 | 관리 UI |

### 현재 동작
~~`claude mcp list` CLI 텍스트 테이블 regex 파싱~~ → ✅ **해결**: `~/.claude/mcp.json` + `.claude/mcp.json` JSON 설정 파일 직접 읽기로 교체. Command, Args, Env, Url 등 전체 서버 정보 획득. `claude mcp add`, `claude mcp remove`는 여전히 CLI 래핑.

### 빠진 것 / 문제점
- ~~**텍스트 테이블 regex 파싱**: CLI 출력 형식 변경 시 즉시 깨짐~~ → ✅ **해결**: JSON 파일 직접 읽기로 교체
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
| `Shared/Services/SpotlightService.cs` | ~310 | 워크트리→리포 실시간 동기화 + 크래시 복구 (Phase 4) |

### 현재 동작
```
시작 (Phase 4 개선):
1. 동시 Spotlight 가드 — 이미 활성 세션 있으면 차단
2. spotlight-state.json에 상태 영속화 (sessionId, originalBranch, repoDir)
3. 메인 리포에서 uncommitted 변경 stash (sessionId별 고유 이름)
4. 세션 브랜치로 checkout
5. 워크트리 파일을 메인 리포로 복사
6. FileSystemWatcher로 워크트리 변경 감시 (500ms 디바운스)
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
- ~~**앱 크래시 시 리포 상태 미복구**: 세션 브랜치에 남아있고, stash가 적용 안 됨~~ → ✅ **해결**: Phase 4에서 `spotlight-state.json` 영속화 + 앱 시작 시 `RecoverAsync()` 자동 복구
- ~~**동시 Spotlight 제한 없음**: 여러 세션이 동시에 활성화하면 리포 충돌~~ → ✅ **해결**: Phase 4에서 `_activeSessionId` 가드 추가. 이미 활성 세션 있으면 차단
- ~~**stash 이름 충돌**: `"cominomi-spotlight-backup"` 고정. 중복 stash/pop 시 문제~~ → ✅ **해결**: Phase 4에서 `cominomi-spotlight-{sessionId}` sessionId별 고유 stash 이름
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

커밋 로그 파싱: `git log --format="%H|%s|%an|%aI"` 파이프 구분자 파싱.

### 빠진 것 / 문제점
- **파이프 구분자 취약**: 커밋 메시지에 `|`가 포함되면 파싱 실패
- **캐싱 없음**: 탭 전환마다 모든 세션의 git log 재실행
- **아카이브 세션 제외**: 과거 활동 이력 조회 불가

---

## 20. 사용량 추적 & 비용 계산

### 관련 파일
| 파일 | 줄수 | 역할 |
|------|------|------|
| `Shared/Services/UsageService.cs` | 196 | JSONL + 비용 계산 |
| `Shared/Models/UsageEntry.cs` | ~77 | 사용량 모델 |
| `Shared/Components/Settings/UsageDashboard.razor` | ~248 | 대시보드 |

### 현재 동작
- JSONL 형식으로 append-only 저장
- SHA-256 기반 중복 제거 (`_recordedHashes` 인메모리 HashSet)
- ~~하드코딩 가격~~ → ✅ `ModelDefinitions.GetPricing(modelId)`으로 위임. 가격은 `ModelPricing` 레코드에 통합
- 기본 내장 가격: Opus $15/$75, Sonnet $3/$15, Haiku $0.80/$4.00 (1M 토큰당). `models.json`으로 변경 가능
- 캐시 할인: write 1.25x, read 0.1x
- 저장 경로: `CominomiConstants.AppName` 상수 사용 (`LocalApplicationData/Cominomi/usage.jsonl`)

### 빠진 것 / 문제점
- ~~**가격 하드코딩**: Anthropic 가격 변경 시 코드 수정 필요~~ → ✅ **해결**: `models.json` 외부 설정 파일로 가격 변경 가능
- **중복 제거 HashSet 앱 재시작 시 리셋**: 재시작 후 같은 세션의 사용량이 이중 기록 가능
- **JSONL 무한 성장**: 정리/로테이션 없음
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

### 빠진 것 / 문제점
- ~~**macOS 구현 없음**: 프로젝트가 MacCatalyst를 타겟하지만, 알림 서비스는 Windows 전용~~ → ✅ **해결**: `UNUserNotificationCenter` 기반 macOS 알림 구현. `IUNUserNotificationCenterDelegate`로 포그라운드 배너 표시 + `NotificationSound` 설정 연동
- **알림 히스토리 없음**: 놓친 알림 확인 불가
- ~~**SnackbarExtensions 한국어 하드코딩**: 12개 알림 메서드의 텍스트가 모두 한국어 문자열. 로컬라이제이션 인프라 없음~~ → ✅ **해결**: `Strings.resx` ResourceManager 기반 로컬라이제이션. 43개 문자열 (ko/en) 지원

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
- ~~**외부 수정 감지 없음**: 다른 프로세스가 settings.json을 수정해도 캐시 무효화 안 됨~~ → ✅ **해결**: `IOptionsMonitor<AppSettings>` 도입 (#136). `AppSettingsChangeNotifier`가 `SaveAsync` 시 change token 발행 → 구독 서비스 자동 갱신
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
- ~~**God Object (5가지 관심사)**~~ → ✅ **해결**: Phase 3에서 메시지/스트리밍/설정을 서브 매니저로 분리. 366→219줄, 관심사 분리 달성
- **Session 가변 참조 공유**: `AppendText()`가 `ChatMessage.Text`와 `ChatMessage.Parts`를 직접 수정. ChatState와 Session이 같은 객체를 참조
- **디바운스 Timer 생성/소멸 반복**: 스트리밍 시작/종료 시 Timer를 만들었다 지웠다 함. 전환 시점에 알림 누락 가능
- **ConsumePendingMessage 1회성**: 두 번 호출하면 두 번째는 null. 메시지 손실 가능
- ~~**인터페이스 없음**: 직접 클래스로 DI 등록. 테스트 시 모킹 불가~~ → ✅ **해결**: `IChatState` 인터페이스 도입 (76줄, `IDisposable` 포함). `builder.Services.AddSingleton<IChatState, ChatState>()` 등록
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

### ~~`async void` 안티패턴 (6개 컴포넌트)~~ → ✅ Phase 3에서 해결

Phase 3에서 6개 컴포넌트의 `async void` 핸들러를 `void` + `InvokeAsync()` 패턴으로 변환:

| 컴포넌트 | Before | After |
|----------|--------|-------|
| `ChatView.razor` | `async void HandleStateChanged()` | `void` + `InvokeAsync(async () => { try {...} catch {...} })` |
| `DetailPanel.razor` | `async void HandleStateChanged()` | `void` + `_ = InvokeAsync(StateHasChanged)` |
| `MainLayout.razor` | `async void HandleSettingsChanged()` | `void` + `InvokeAsync(async () => { try {...} catch {...} })` |
| `SessionList.razor` | `async void OnStateChanged()` | `void` + `_ = InvokeAsync(() => {...})` |
| `SidebarChanges.razor` | `async void HandleStateChanged()` | `void` + `_ = InvokeAsync(StateHasChanged)` |
| `SidebarExplorer.razor` | `async void HandleStateChanged()` | `void` + `_ = InvokeAsync(StateHasChanged)` |

**참고**: 모든 컴포넌트에서 `Dispose()`에서 `OnChange -= handler`로 구독 해제는 올바르게 구현됨.

### ClaudeService 예외 삼킴 (5곳)

`ClaudeService.cs`에서 프로세스 생명주기 관리를 위해 `catch { }` 패턴 사용:
- `:158` — ExitCode 접근 시 프로세스 이미 종료
- `:170` — `process.Dispose()` 실패 무시
- `:224`, `:231` — `process.Kill(entireProcessTree: true)` 실패 무시
- `:334`, `:369` — 재시도/AgentProcess에서 동일 패턴

프로세스 정리 시 예외 발생은 합리적이나, 일괄 무시로 디버깅 정보 손실.

### 빠진 것 / 문제점
- ~~**중앙 에러 전략 없음**: 각 서비스가 독자적 방식~~ → ✅ **해결**: `AppError` 레코드 + `ErrorCode` enum (WorktreeCreationFailed, BranchPushRejected, PrMergeConflict 등) + 에러 카테고리 (Unknown, Transient, Permanent) + `ClassifyPushError()`/`ClassifyMergeError()` 분류 메서드. `SessionGitWorkflowService`에서 활용
- ~~**에러 코드/타입 없음**~~ → ✅ **해결**: 위 `ErrorCode` enum 참조
- ~~**transient vs permanent 구분 없음**~~ → ✅ **해결**: `ErrorCategory` enum으로 분류. Transient 에러는 재시도 가능
- ~~**async void 예외 미관찰**: 위 표 참조. 6개 컴포넌트에서 반복~~ → ✅ **해결**: Phase 3에서 전부 수정 (위 표 참조)
- **프로세스 stderr 파싱 ad-hoc**: git/gh/claude 각각 다른 방식으로 에러 텍스트 해석
- ~~**로컬라이제이션 인프라 부재**: `.resx` 파일 0개~~ → ✅ **해결**: `Strings.resx` (ko) + `Strings.en.resx` (en) — 43개 문자열. `ResourceManager` 기반 정적 접근자 패턴. SnackbarExtensions, ToolDisplayHelper, QuestionDetector 전부 마이그레이션 완료

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
- ~~**ChatView가 커플링 허브 (18개 서비스)**~~ → ✅ **해결**: Phase 3에서 14개, Phase 6에서 13개로 축소. 추출된 서비스(SystemPromptBuilder, SessionInitializer, ChatPrWorkflowService)가 중간 계층 역할. BranchSelector/SessionWorkflowBar/LandingPage 자식 컴포넌트로 UI 분리
- **미디에이터 패턴 없음**: 모든 통신이 직접 서비스 호출 + ChatState.OnChange
- **같은 세션을 여러 경로로 로드**: ChatView와 SessionGitWorkflow가 독립적으로 `LoadSessionAsync` 호출. 다른 인스턴스를 가지고 작업할 수 있음
- ~~**테스트 0개**: 전체 코드베이스에 단위/통합 테스트 없음~~ → ✅ **해결**: Phase 4+에서 `tests/Cominomi.Shared.Tests/` 프로젝트 도입. 84개 테스트 (6개 테스트 파일). `InternalsVisibleTo`로 internal 메서드 테스트 가능

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

# 해결된 구조적 문제 (Phase 1–6)

| 문제 | 해결 방법 | Phase |
|------|-----------|-------|
| ChatView God Component (1,461줄, 18개 서비스) | `StreamEventProcessor` + `SystemPromptBuilder` + `SessionInitializer` + `ChatPrWorkflowService` + `BranchSelector` + `SessionWorkflowBar` + `LandingPage` 추출. 545줄, 13개 서비스 | 1, 3, 6 |
| JSON 영속화 파일 락 없음 | `AtomicFileWriter` + per-session `SemaphoreSlim` 락 | 1 |
| 세션 파일 무한 성장 | 메타데이터/메시지 분리 저장 + 도구 출력 2,000자 절단 | 4 |
| ChatState God Object (366줄) | 파사드 패턴 + `MessageManager`/`StreamingStateManager`/`SettingsStateManager`. 219줄 | 3 |
| 상태 전이 검증 없음 | `SessionStatusMachine` + `Session.TransitionStatus()` + `private set` | 4 |
| 프로세스 실행 복붙 (7개 서비스) | `IProcessRunner` 통합 마이그레이션 | 2 |
| SessionList God Component (719줄) | `SessionItem`+`SessionListToolbar`+`SessionListDataService` 추출. 439줄 | 3, 4 |
| PR 생성 경로 2개 | `IChatPrWorkflowService`로 통합 | 3 |
| 가격/모델/CLI 하드코딩 | `ModelDefinitions` + `models.json` + `CominomiConstants` | 2 |
| 테스트 0개 | `tests/Cominomi.Shared.Tests/` — 84개 테스트 | 4+ |
| `async void` 안티패턴 (6개 컴포넌트) | `void` + `InvokeAsync()` 안전 패턴 | 3 |
| Spotlight 크래시 시 리포 미복구 | `spotlight-state.json` + `RecoverAsync()` + 동시 가드 + sessionId별 stash | 4 |
| 로컬라이제이션 인프라 부재 | `Strings.resx` (ko/en) + `ResourceManager` — 43개 문자열 | 6 |
| ChatState 인터페이스 없음 | `IChatState` 인터페이스 (76줄) + DI 등록 변경 | 6 |
| GetSessionsAsync O(n) 성능 | `ConcurrentDictionary` 인메모리 캐시 + 병렬 I/O | 6 |
| Graceful shutdown 없음 | `App.xaml.cs` CleanUp() + IDisposable 서비스 정리 | 6 |
| Git 워크플로우 롤백/리베이스 없음 | `RebaseInternalAsync()` + `ClosePrInternalAsync()` 자동 복구 | 6 |
| 중앙 에러 전략 없음 | `AppError` + `ErrorCode` enum + Transient/Permanent 분류 | 6 |
| MCP 서비스 regex 파싱 | `mcp.json` 직접 읽기로 교체 | 6 |
| Session 모델 3관심사 혼재 | `GitContext` + `PrContext` 분리 + `SessionJsonConverter` 하위호환 | 6 |

---

# 구조적 문제 Top 10 — 전항목 해결 완료 ✅

> Phase 6 (PRs #122–#131)에서 기존 Top 10의 모든 항목이 해결되었습니다.

| 순위 | 문제 | 해결 방법 | PR |
|------|------|-----------|-----|
| ~~**1**~~ | ~~로컬라이제이션 인프라 부재~~ | `Strings.resx` (ko) + `Strings.en.resx` (en) — 43개 문자열. `ResourceManager` 정적 접근자 패턴 | #127 |
| ~~**2**~~ | ~~ChatView 899줄 / 14개 서비스~~ | `BranchSelector`(106줄) + `SessionWorkflowBar`(205줄) + `LandingPage`(93줄) 추출. 899→545줄, 14→13서비스 | #130 |
| ~~**3**~~ | ~~Session 모델 3관심사 혼재~~ | `GitContext` + `PrContext` 분리 + `SessionJsonConverter` 하위호환 | #129 |
| ~~**4**~~ | ~~중앙 에러 전략 없음~~ | `AppError` 레코드 + `ErrorCode` enum + Transient/Permanent 분류 + `ClassifyPushError()`/`ClassifyMergeError()` | #131 |
| ~~**5**~~ | ~~ChatState 인터페이스 없음~~ | `IChatState` 인터페이스 (76줄, `IDisposable`) + DI 등록 변경 | #128 |
| ~~**6**~~ | ~~GetSessionsAsync O(n) 성능~~ | `ConcurrentDictionary` 인메모리 캐시 + `EnsureCacheLoadedAsync()` 1회 로드 + 병렬 I/O | #122 |
| ~~**7**~~ | ~~Graceful shutdown 없음~~ | `App.xaml.cs` CleanUp() — IClaudeService, ISpotlightService, ChatState, SessionListDataService Dispose + Log.CloseAndFlush() | #123 |
| ~~**8**~~ | ~~Git 워크플로우 롤백/리베이스 없음~~ | `RebaseInternalAsync()` 자동 리베이스 + `ClosePrInternalAsync()` PR 자동 닫기 | #125 |
| ~~**9**~~ | ~~플러그인 시스템 미구현~~ | 매니페스트 파싱 + 상태 관리 + enable/disable + 실행 엔진 구현 완료 (EntryPoint 로딩/실행/샌드박싱 + hooks·skills 등록) | #126, #137 |
| ~~**10**~~ | ~~MCP 서비스 regex 파싱~~ | `~/.claude/mcp.json` + `.claude/mcp.json` JSON 직접 읽기. regex 파싱 제거 | #124 |

### 차기 개선 후보 — 전항목 해결 완료 ✅

| 순위 | 문제 | 해결 방법 | PR |
|------|------|-----------|-----|
| ~~**1**~~ | ~~플러그인 실행 엔진~~ | EntryPoint 로딩/실행/샌드박싱 + hooks·skills 매니페스트 자동 등록 | #137 |
| ~~**2**~~ | ~~같은 세션 3~4회 로드~~ | Internal 메서드 + `LoadSessionAsync` 2초 TTL 캐시 | #134 |
| ~~**3**~~ | ~~MainLayout 과다 책임~~ | `IThemeService` 추출 + `SidebarToolbar`/`MainToolbar` 컴포넌트 분리. 238→~127줄(47%↓) | #135 |
| ~~**4**~~ | ~~macOS 알림 미구현~~ | `UNUserNotificationCenter` 기반 macOS 알림 + 포그라운드 배너 + `NotificationSound` 설정 연동 | #133 |
| ~~**5**~~ | ~~옵션 패턴 미사용~~ | `IOptionsMonitor<AppSettings>` + `AppSettingsFactory` + `AppSettingsChangeNotifier`. 8개 서비스/컴포넌트 전환 | #136 |

---

# 신규 구조적 문제 Top 10

> 기존 Top 10 + 차기 후보 전항목 해결 후, 코드베이스 전체 재분석으로 도출한 신규 Top 10입니다. (미해결 문제 82건 중 영향도·빈도·심각도 기준 선정)

| 순위 | 문제 | 영향 | 관련 섹션 | 난이도 |
|------|------|------|-----------|--------|
| **1** | **빈 catch 블록 14곳** — `catch { }` 로 에러 삼킴 | 디버깅 불가. 도구 위젯 7개 + ChatView + InputArea + SessionWorkflowBar + ClaudeService 등. 프로덕션에서 원인 추적 불가 | §10, §13, §7 | 낮 |
| ~~**2**~~ | ~~**비원자적 파일 쓰기 5곳** — `AtomicFileWriter` 미사용~~ | ~~`SpotlightService` 상태, `ContextService` notes/todos/plans, `AttachmentService` .gitignore, `UsageService` JSONL — 앱 크래시 시 데이터 손상 가능~~ | §19, §17, §20 | ✅ 해결 |
| **3** | **ClaudeService 재시도 60줄 복붙** — `--verbose` 재시도 시 스트리밍 루프 전체 복사 | 유지보수 시 두 곳을 동시 수정해야 함. 하나만 수정 시 동작 불일치 | §7 | 중 |
| **4** | **데이터 모델 불변성 부재** — 모든 모델이 `public set`, 가변 참조 공유 | `ChatState`와 `Session`이 같은 `ChatMessage` 객체 참조. `AppendText()`가 `Text` + `Parts` 양쪽 수정. 상태 추적 불가 | §3, §23 | 높 |
| **5** | **Git/Activity 캐싱 없음** — 매번 프로세스 생성 | `DetectDefaultBranchAsync`, `ListBranchesAsync`가 매 호출마다 git 프로세스. 탭 전환마다 모든 세션의 `git log` 재실행 | §5, §19.5 | 중 |
| **6** | **시스템 프롬프트·메모리 크기 제한 없음** — 무제한 토큰 소비 | notes.md 10만줄이 그대로 프롬프트에 주입. 메모리도 전체 JSON 로드 + 워크스페이스별 필터링 없음 | §17, §18 | 중 |
| **7** | ~~**app.css 3,169줄 모놀리스** — 토큰·컴포넌트 스타일 혼재~~ ✅ **해결** | `tokens.css` 분리 완료 (73줄). `app.css` → 3,095줄 (컴포넌트 전용). `index.html`에서 `tokens.css` → `app.css` 순서 로드 | §11 | 낮 |
| **8** | **SessionList 가상화 없음** — 전체 DOM 렌더링 | 세션 수십 개 이상에서 사이드바 렌더 성능 저하. `<Virtualize>` 미적용 | §12 | 낮 |
| **9** | **입력 검증 전면 부재** — 설정/세션/플러그인 경계에서 | 빈 `WorkspaceId`로 Session 생성 가능, 음수 토큰 허용, 잘못된 경로의 설정 저장 가능. 5+ public 메서드에 null 체크 없음 | §3, §22 | 중 |
| **10** | **JSONL 사용량 로그 무한 성장** — 로테이션/정리 없음 | `usage.jsonl` 무한 증가. 중복 제거 `HashSet` 앱 재시작 시 리셋 → 이중 기록. 내보내기 기능도 없음 | §20 | 낮 |

### 차기 개선 후보

| 순위 | 문제 | 영향 | 관련 섹션 | 난이도 |
|------|------|------|-----------|--------|
| **1** | **훅 엔진 개선** | 5초 타임아웃 하드코딩 + 직렬 실행 + 출력 미캡처 + 커맨드 이스케이핑 최소 | §14 | 중 |
| **2** | **패널 크기 조절 불가** | CSS 고정 너비. 사이드바·디테일 패널 리사이즈 불가 + 키보드 네비게이션 없음 | §11 | 중 |
| **3** | **JSON 스키마 마이그레이션 없음** | 스키마 변경 시 기존 파일 역직렬화 실패 → 데이터 손실. 버전 필드 없음 | §2 | 높 |
| **4** | **~~워크트리 레이스 컨디션~~** | ~~Pending 세션에 빠르게 2번 전송 시 워크트리 이중 생성 시도 가능~~ ✅ 해결: per-session SemaphoreSlim + UI 가드 | §8 | 중 |
| **5** | **활동 서비스 파이프 구분자 취약** | 커밋 메시지에 `\|`가 포함되면 파싱 실패. 아카이브 세션 활동 조회 불가 | §19.5 | 낮 |

---

> 이 문서의 각 섹션을 가리켜 변경 방향을 지시하세요. 예:
> - "Top 1: 빈 catch 블록 정리 — 로깅 추가 또는 의도적 무시 주석"
> - "Top 4: Session/ChatMessage를 record 기반 불변 모델로 전환"
> - "Top 8: SessionList에 `<Virtualize>` 컴포넌트 적용"
