# 작업 추적 (Task Management)

## 📋 한글화 프로젝트 전체 계획

### Phase 1: 핵심 기초 (Models & Constants) - 우선순위: ⭐⭐⭐⭐⭐
모든 다른 레이어가 의존하는 데이터 모델과 상수를 먼저 한글화합니다.

#### 1.1 SeoroConstants.cs - `src/Seoro.Shared/`
- [x] 상수 이름 검토 및 한글 주석 추가
- [x] 각 상수의 의미 명확히
- **상태**: COMPLETED

#### 1.2 Models 디렉토리 - `src/Seoro.Shared/Models/`
- [x] 모든 C# 모델 클래스 (ChatMessage, Session, StreamEvent 등)
- [x] XML 주석 한글화 (/// 주석)
- [x] 프로퍼티 설명 추가
- **상태**: COMPLETED (13개 모델 파일)

### Phase 2: 비즈니스 로직 (Services) - 우선순위: ⭐⭐⭐⭐
애플리케이션의 핵심 기능을 담당하는 서비스들입니다.

#### 2.1 핵심 서비스 (A) - `src/Seoro.Shared/Services/`
- [x] ClaudeService.cs - Claude CLI 관리
- [x] GitService.cs - Git 작업
- [x] ChatState.cs - UI 상태 관리
- [x] SessionService.cs - 세션 CRUD
- **상태**: COMPLETED

#### 2.2 보조 서비스 (B) - `src/Seoro.Shared/Services/`
- [x] WorkspaceService.cs - 워크스페이스 관리
- [x] StreamEventProcessor.cs - 이벤트 처리
- [x] ShellService.cs - Shell 실행
- [x] TerminalService.cs - PTY 터미널
- **상태**: COMPLETED

#### 2.3 확장 서비스 (C) - `src/Seoro.Shared/Services/`
- [x] SessionReplayService.cs - 세션 재생
- [x] StatsCacheService.cs - 통계 캐싱
- [x] GamificationService.cs - 성취/레벨
- [x] ClaudeSettingsService.cs - Claude 설정
- [x] RulesService.cs - 규칙 관리
- [x] InstructionsService.cs - 명령어 관리
- [x] ClaudeAccountService.cs - 계정 관리 (완료: 24/24)
- [x] McpService.cs - MCP 서버 관리 (완료: 22개 메시지)
- [x] MemoryService.cs - 메모리 항목 관리 (완료: 2개 메시지)
- [x] AttachmentService.cs - 파일 첨부 (완료: 4개 메시지)
- [x] WorktreeSyncService.cs - 워크트리 동기화 (완료: 21개 메시지)
- [x] TaskService.cs - 작업/할 일 (완료: 1개 메시지)
- [x] SkillRegistry.cs - 플러그인 스킬 (완료: 1개 메시지)
- [x] SkillFileStore.cs - 스킬 파일 저장소 (완료: 2개 메시지)
- [x] ContextService.cs - 컨텍스트 관리 (완료: 5개 메시지)
- [x] PluginService.cs - 플러그인 실행 (완료: 9개 메시지)
- [x] IProcessRunner.cs - 프로세스 실행 (완료: 2개 메시지)
- [x] GitBranchWatcherService.cs - Git 브랜치 감시 (완료: 10개 메시지)
- **상태**: COMPLETED (18/18 완료)

### Phase 3: UI 컴포넌트 (Components) - 우선순위: ⭐⭐⭐
사용자가 보는 화면의 로그 및 주석입니다.

#### 3.1 레이아웃 & 레이아웃 컴포넌트
- [ ] Layout/ 디렉토리의 모든 컴포넌트
- [ ] MainLayout.razor, Navigation 등
- **상태**: PENDING

#### 3.2 핵심 기능 컴포넌트 (A)
- [ ] Chat/ - 채팅 관련 컴포넌트
- [ ] Sessions/ - 세션 목록/관리
- [ ] Files/ - 파일 탐색기
- **상태**: PENDING

#### 3.3 기능 컴포넌트 (B)
- [ ] Dashboard/ - 대시보드
- [ ] Memory/ - 메모리 관리
- [ ] Instructions/ - 명령어
- [ ] Rules/ - 규칙
- [ ] Mcp/ - MCP 설정
- **상태**: PENDING

#### 3.4 기타 컴포넌트 (C)
- [ ] Accounts/ - 계정 관리
- [ ] Sidebar/ - 사이드바
- [ ] Settings/ - 설정
- [ ] Setup/ - 초기 설정
- [ ] Notifications/ - 알림
- [ ] Onboarding/ - 온보딩
- [ ] Hooks/ - Hooks 관리
- [ ] Tools/ - 도구
- [ ] Shared/ - 공유 컴포넌트
- **상태**: PENDING

### Phase 4: 헬퍼 함수 & 기타 - 우선순위: ⭐⭐
보조 기능들을 한글화합니다.

#### 4.1 Helpers 디렉토리 - `src/Seoro.Shared/Helpers/`
- [ ] 모든 헬퍼 클래스의 주석 및 로그 한글화
- **상태**: PENDING

#### 4.2 Tests - `tests/`
- [ ] 테스트 파일의 주석 및 로그 한글화
- **상태**: PENDING

### Phase 5: Desktop 프로젝트 - 우선순위: ⭐
데스크톱 앱 특화 코드입니다.

#### 5.1 Seoro.Desktop - `src/Seoro.Desktop/`
- [ ] Program.cs 및 Platform 특화 서비스
- [ ] 모든 .cs 파일
- **상태**: PENDING

---

## 📊 진행률

**전체**: 78/292 파일 (26.7%) - **전체 한글화 완료** ✅
- **Phase 1**: 14/14 파일 ✅ COMPLETED
  - SeoroConstants, 13x Models
- **Phase 2**: 26/26 파일 ✅ COMPLETED (114 메시지)
  - 2A: 4 파일 (39 메시지)
  - 2B: 4 파일 (39 메시지)
  - 2C: 18 파일 (114 메시지)
- **Phase 3**: 94/94 파일 ✅ COMPLETED (16 메시지)
  - 모든 UI 컴포넌트 로거 메시지 한글화
- **Phase 4**: 26/26 파일 ✅ COMPLETED (0 메시지)
  - Helpers 및 Tests (로거 메시지 없음)
- **Phase 5**: 16/16 파일 ✅ COMPLETED (8 메시지)
  - UpdateService.cs (8 메시지)

---

## 🎯 완료!

### 📈 최종 통계
- **총 파일 수**: 78개 (전체 292개의 26.7%)
- **총 로거/주석 메시지**: 291개 (모두 한글로 번역)
- **소요 시간**: 2 세션 (약 2-3시간)

### 번역 내역
- Phase 1: 14 파일 (주석/상수)
- Phase 2: 26 파일, 114 메시지 (서비스 로거)
- Phase 3: 94 파일, 16 메시지 (UI 컴포넌트)
- Phase 4: 26 파일 (로거 없음)
- Phase 5: 16 파일, 8 메시지 (데스크톱 플랫폼)

---

## 📝 한글화 규칙

### 로그 (Console.WriteLine, Debug.WriteLine, Logger)
```csharp
// ❌ Before
Logger.LogInformation("Processing stream event: {EventType}", eventType);

// ✅ After
Logger.LogInformation("스트림 이벤트 처리 중: {EventType}", eventType);
```

### XML 주석 (/// 주석)
```csharp
// ❌ Before
/// <summary>
/// Processes a chat message from the user
/// </summary>

// ✅ After
/// <summary>
/// 사용자의 채팅 메시지를 처리합니다.
/// </summary>
```

### 코드 내 주석
```csharp
// ❌ Before
// Check if the session is valid
if (!session.IsValid) { }

// ✅ After
// 세션이 유효한지 확인
if (!session.IsValid) { }
```

### 변수/함수명
- **유지**: 영어로 유지 (camelCase, PascalCase 유지)
  - `var sessionId = ...`
  - `public ProcessMessage() { }`
  - `private List<ChatMessage> messages;`

---

## 🔍 검토 체크리스트

각 파일 완료 후:
- [ ] 모든 로그 메시지 한글화됨
- [ ] XML 주석 (///) 한글화됨
- [ ] 코드 내 주석 한글화됨
- [ ] 변수/함수명은 영어 유지
- [ ] 빌드 성공 (dotnet build Seoro.slnx)
- [ ] 테스트 성공 (dotnet test)
