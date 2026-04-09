# CLAUDE.md

## Critical Context
- **Tech Stack**: .NET 10.0 + Blazor + Photino.Blazor (desktop) + MudBlazor v9 (UI)
- **Solution**: `Seoro.slnx` (new XML solution format)
- **Projects**:
  - `src/Seoro.Desktop` - Desktop app entry point (WinExe, Photino window)
  - `src/Seoro.Shared` - Shared library (models, services, Razor components)
  - `tests/Seoro.Shared.Tests` - xUnit test suite
- **Platform**: Windows x64, macOS ARM64
- **Build**: `dotnet build Seoro.slnx`, release via `dotnet publish` + Velopack
- **Test**: `dotnet test`
- **Auto-update**: Velopack v0.0.1298 (`vpk` tool)
- **CI/CD**: GitHub Actions (`release.yml`) - triggered on `v*` tags
- **Required CLI**: Claude CLI >= 2.1.81
- **Required Runtime**: Node.js 20 (CI/CodeMirror 빌드)
- **Codebase**: ~51,600 lines (.cs, .razor, .css, .js)
- **Latest Version**: 1.17.7 (2026-04-09)
- **Single Instance**: Mutex(`SeoroSingleInstance`) — 다중 실행 방지

## Architecture Overview
Seoro is a cross-platform desktop GUI client for Claude Code (Anthropic's CLI).
It wraps the Claude CLI process and provides a rich Blazor-based UI for chat sessions,
git operations, file management, plugins, and more.

```
src/
  Seoro.Desktop/              # 데스크톱 앱 진입점 (10 서비스)
    Program.cs                    # Main entry - DI 컨테이너, Photino 윈도우 초기화
    Services/                     # 플랫폼별 서비스 (아래 Desktop Services 참고)
  Seoro.Shared/                # 핵심 로직 (플랫폼 독립)
    Models/                       # 데이터 모델 43개 + ViewModels/ 4개
    Services/                     # 비즈니스 로직 106개 파일
      Migration/                    # JSON 스키마 마이그레이션 (6개)
      StreamEventHandlers/          # 스트림 이벤트 핸들러 파이프라인 (12개)
    Components/                   # Blazor 컴포넌트 99개 (18개 폴더)
      Accounts/ Chat/ Dashboard/ Files/ Hooks/ Instructions/
      Layout/ Mcp/ Memory/ Notifications/ Onboarding/ Rules/
      Sessions/ Settings/ Settings/Sections/ Setup/ Shared/ Sidebar/ Tools/
    SeoroConstants.cs          # 공유 상수 및 제한값
tests/
  Seoro.Shared.Tests/          # 유닛 테스트 (xUnit)
```

## Key Services (Shared)

### Core
- `ClaudeService` - Claude CLI 프로세스 관리, 스트리밍 JSON 이벤트 처리
- `ClaudeCliResolver` - Claude CLI 실행 파일 탐색 및 버전 확인
- `ClaudeArgumentBuilder` - Claude CLI 인수 조합
- `ProcessRunner` - 프로세스 실행 추상화 (IProcessRunner)
- `ProcessErrorClassifier` - 프로세스 오류 분류 및 사용자 친화적 메시지
- `ShellService` - 셸 실행, macOS 기본 셸 자동 감지
- `DependencyCheckService` - Claude CLI 설치 여부 및 버전 검증

### Chat & Streaming
- `ChatState` - UI 상태 관리 (메시지, 스트리밍, 탭)
- `ChatEventBus` - 채팅 이벤트 발행/구독 (pub-sub)
- `ChatMessageOrchestrator` - 메시지 송수신 조율
- `MessageManager` - 메시지 저장/로드/삭제
- `StreamEventProcessor` - 스트림 이벤트 파이프라인 디스패치
- `StreamingStateManager` - 스트리밍 상태 추적 (시작/진행/완료)
- `ContentGrouper` - 연속 콘텐츠 블록 그룹화
- `TabManager` - 채팅 탭 관리
- `SystemPromptBuilder` - 시스템 프롬프트 조립 (토큰 예산 관리)

### Stream Event Handlers (12개)
- `SystemInitHandler` → `MessageStartHandler` → `ContentBlockStartHandler` → `ContentBlockDeltaHandler` → `ContentBlockStopHandler` → `MessageDeltaHandler` → `AssistantMessageHandler` → `UserMessageHandler` → `ResultHandler` → `ErrorHandler`

### Session & Workspace
- `SessionService` - 세션 CRUD 및 영속화
- `SessionInitializer` - 새 세션 초기화 로직
- `SessionStatusMachine` - 세션 상태 머신 (idle/running/error)
- `SessionReplayService` - 세션 타임라인 리플레이 및 내비게이션
- `SessionListDataService` / `SessionListFacade` - 세션 목록 데이터 및 UI 파사드
- `ActiveSessionRegistry` - 활성 세션 레지스트리 (동시 세션 제한)
- `WorkspaceService` - 워크스페이스 (Git 저장소) 관리
- `StatsCacheService` - 사용량 통계 캐싱 및 집계 (stats-cache.json)

### Git
- `GitService` - Git 작업 (clone, diff, branches) + 캐싱
- `GitBranchWatcherService` - 실시간 Git 브랜치 추적 및 변경 감지
- `WorktreeSyncService` - Git 워크트리 동기화
- `DiffParser` - Diff 출력 파싱

### Configuration & Settings
- `SettingsService` - 앱 설정 CRUD
- `SettingsStateManager` - 설정 변경 추적
- `SettingsValidator` - 설정값 유효성 검증
- `AppSettingsFactory` - AppSettings 생성
- `AppSettingsChangeNotifier` - 설정 변경 알림 발행
- `ClaudeSettingsService` - Claude CLI 설정 (permissions, env vars, MCP) 관리
- `ThemeService` - 다크/라이트 테마 관리

### Content & Knowledge
- `RulesService` - 프로젝트 규칙 파일 CRUD
- `InstructionsService` - 인스트럭션 파일 관리
- `MemoryService` - 영구 메모리 항목 관리 (User/Feedback/Project/Reference)
- `ContextService` - 컨텍스트 항목 관리 (notes, todos, plans)
- `TaskService` - 세션 내 태스크/할일 관리
- `McpService` - MCP 서버 설정 및 관리 (standalone page)

### Account & Auth
- `ClaudeAccountService` - 멀티 계정 관리 (전환, 등록, 삭제)
- `ClaudeCredentialService` - 자격 증명 저장/로드

### Plugin System
- `PluginService` - 플러그인 로딩
- `PluginExecutionEngine` - 플러그인 실행 엔진
- `SkillRegistry` / `SkillFileStore` - 스킬 탐색 및 실행
- `HooksEngine` / `HooksFileEnvelope` - 커스텀 훅 실행 엔진

### Gamification & Notification
- `GamificationService` - 업적 계산, 스트릭, 레벨 진행 (9카테고리, 105업적)
- `NotificationHistoryService` - 알림 이력 관리
- `ReleaseNotesService` (Desktop) - 인앱 릴리스 노트 (changelog.json)

### Infrastructure
- `AtomicFileWriter` - 원자적 파일 쓰기 (임시파일 → rename)
- `JsonDefaults` - 공유 JSON 직렬화 옵션
- `VersionComparer` - 시맨틱 버전 비교
- `LightboxService` - 이미지 라이트박스
- `ToolDisplayHelper` - 도구 호출 표시 포매팅
- `SnackbarExtensions` - MudBlazor 스낵바 확장

### Migration
- `SchemaMigrator` / `SchemaMigratorRegistry` - JSON 스키마 마이그레이션 프레임워크
- `MigratingJsonReader` / `MigratingJsonWriter` - 마이그레이션 적용 JSON 읽기/쓰기
- `CominomiMigrationService` (Desktop) - Cominomi → Seoro 데이터 마이그레이션

## Desktop Services (10개, `src/Seoro.Desktop/Services/`)
- `FilePickerService` / `FolderPickerService` / `SaveFilePickerService` - 파일/폴더 선택 대화상자
- `NotificationService` - OS 네이티브 알림
- `UpdateService` - Velopack 자동 업데이트
- `ReleaseNotesService` - 릴리스 노트 로드
- `LauncherService` - 외부 URL/파일 실행
- `PhotinoWindowHolder` - Photino 윈도우 참조 관리
- `WindowCloseGuardService` - 활성 세션 시 종료 방지
- `DeferredSnackbarService` - 지연 스낵바 표시

## Models (43개 + ViewModels 4개)

### Core Models
- `Session` / `SessionJsonConverter` - 세션 데이터 및 JSON 변환
- `ChatMessage` - 채팅 메시지 (역할, 콘텐츠, 도구 호출)
- `StreamEvent` / `StreamingPhase` - 스트림 이벤트 및 단계
- `ToolCall` - 도구 호출 정보
- `Workspace` / `WorkspacePreferences` - 워크스페이스 및 설정
- `AppSettings` - 앱 전역 설정
- `AppError` - 에러 정보

### Domain Models
- `ClaudeAccount` / `ClaudeAccountStore` / `AccountUsageInfo` - 계정
- `ClaudeSettings` / `CliCapabilities` - CLI 설정 및 기능
- `ModelDefinitions` - 지원 모델 목록
- `GamificationModels` - 레벨, 업적, 스트릭
- `StatsCacheModels` - 통계 캐시
- `SessionReplayModels` - 리플레이
- `GitContext` / `GitRepoInfo` / `BranchInfo` / `DiffInfo` - Git
- `FileNode` / `FileAttachment` - 파일
- `McpServer` - MCP 서버
- `MemoryEntry` - 메모리 항목
- `HookDefinition` - 훅 정의
- `RuleFile` / `InstructionFile` - 규칙/인스트럭션
- `SkillDefinition` / `SkillChainStep` - 스킬
- `TemplateDefinition` - 템플릿
- `TaskItem` - 태스크
- `ContextInfo` - 컨텍스트
- `NotificationRecord` - 알림
- `ReleaseNote` - 릴리스 노트
- `SyncState` - 동기화 상태
- `UsageEntry` - 사용량
- `AgentType` / `ConventionalCommitType` / `RightPanelMode` - 열거형
- `CityNames` - 세션 자동 이름용 도시명

### ViewModels (`Models/ViewModels/`)
- `ActivitySummaryInfo` - 활동 요약
- `ContentGroup` - 콘텐츠 그룹
- `MainTab` - 메인 탭
- `PlanReviewAction` - 플랜 리뷰 액션

## Key Constants (`SeoroConstants.cs`)
- `MaxActiveSessionsPerWorkspace`: 20
- `MaxContextItemTokens`: 2,000 (단일 메모/할일/플랜)
- `MaxContextPromptTokens`: 5,000 (notes + todo + plans 합계)
- `MaxMemoryEntryTokens`: 1,000 (단일 메모리 항목)
- `MaxMemoryPromptTokens`: 2,500 (전체 메모리 합계)
- `MaxSystemPromptTokens`: 10,000 (시스템 프롬프트 총 예산)
- `DefaultPermissionMode`: `bypassAll`
- `BranchPrefix`: `seoro/`
- `DefaultEffortLevel`: `auto`
- `PasteAsFileThreshold`: 500 (붙여넣기 자동 파일 변환 기준)
- `RequiredClaudeVersion`: `2.1.81`
- `ShellCacheTtl`: 10분
- `WhichTimeout`: 5초

## CLI Tools (dotnet-tools.json)
- `ilspycmd` v9.1.0.7988 - IL 디컴파일러
- `vpk` v0.0.1298 - Velopack 패키징

## Development Workflow
1. `dotnet tool restore` - CLI 도구 복원 (ilspycmd, vpk)
2. `dotnet build Seoro.slnx` - 전체 빌드
3. `dotnet test` - 테스트 실행
4. `dotnet run --project src/Seoro.Desktop` - 앱 실행
5. Release: `v*` 태그 push → GitHub Actions CI/CD 트리거

## CI/CD Pipeline (`release.yml`)
1. **release-windows** (windows-latest): .NET 10 + Node.js 20 → `dotnet publish` win-x64 → Velopack 패키징
2. **release-macos** (macos-15): .NET 10 + Node.js 20 → `dotnet publish` osx-arm64 → .app 번들 생성 → Velopack 패키징
3. **publish** (ubuntu-latest): 두 플랫폼 완료 후 릴리스를 non-draft로 전환
- 델타 업데이트 지원 (이전 릴리스 다운로드 → 차분 생성)
- 버전은 태그에서 추출하여 `-p:Version`으로 주입

## Release Checklist
버전 태그(`vX.Y.Z`)를 push하기 전에 반드시 아래 항목을 확인:
1. `changelog.json`에 해당 버전의 변경 내역 추가
2. `README.md` 등 문서에 버전 관련 내용이 있다면 최신화
3. CI가 태그에서 버전을 추출하여 어셈블리에 자동 반영 (`-p:Version`)

## Anti-Patterns
- `SeoroConstants.cs` 제한값을 토큰 예산 영향 분석 없이 수정 금지
- `StreamEventProcessor` 파이프라인 우회 금지 — 새 핸들러를 추가할 것
- Blazor 컴포넌트에서 동기 I/O 금지 — 반드시 async 사용
- 플랫폼 경로 하드코딩 금지 — 서비스 추상화 사용
- `AtomicFileWriter` 우회하여 직접 파일 쓰기 금지 — 데이터 손실 위험
- `ActiveSessionRegistry` 무시하고 세션 생성 금지 — 동시 세션 제한 준수
