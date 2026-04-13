<div align="center">
  <img src="src/Seoro.Desktop/wwwroot/icon-192.png" width="120" alt="Seoro" />
  <h1>Seoro</h1>
  <p><b>Claude Code 데스크톱 GUI 클라이언트</b></p>
  <p><sub>A cross-platform desktop GUI client for <a href="https://docs.anthropic.com/en/docs/claude-code">Claude Code</a> by Anthropic</sub></p>

  <br>

  <a href="https://github.com/JinoPay/Seoro/releases/latest"><img src="https://img.shields.io/github/v/release/JinoPay/Seoro?style=flat-square&label=%EB%A6%B4%EB%A6%AC%EC%8A%A4&color=blue" alt="Latest Release" /></a>
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 10.0" />
  <img src="https://img.shields.io/badge/Blazor-512BD4?style=flat-square&logo=blazor&logoColor=white" alt="Blazor" />
  <img src="https://img.shields.io/badge/Windows-x64-0078D4?style=flat-square&logo=windows11&logoColor=white" alt="Windows x64" />
  <img src="https://img.shields.io/badge/macOS-ARM64-000000?style=flat-square&logo=apple&logoColor=white" alt="macOS ARM64" />
  <a href="https://github.com/JinoPay/Seoro/stargazers"><img src="https://img.shields.io/github/stars/JinoPay/Seoro?style=flat-square&label=%E2%AD%90" alt="Stars" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/%EB%9D%BC%EC%9D%B4%EC%84%A0%EC%8A%A4-Apache%202.0-blue?style=flat-square" alt="License" /></a>
</div>

<br>

> **Seoro**는 Anthropic의 [Claude Code](https://docs.anthropic.com/en/docs/claude-code) CLI를 위한 크로스플랫폼 데스크톱 GUI 클라이언트입니다.
> 멀티세션 스트리밍 채팅, 분석 대시보드, 게이미피케이션, 내장 터미널, Git 워크플로우 통합 등 —
> Claude Code의 모든 잠재력을 직관적인 UI에서 끌어냅니다.

<br>

<!-- ────────────────────── 스크린샷 ────────────────────── -->

<div align="center">

  <!-- 스크린샷을 추가하려면 docs/screenshots/ 디렉터리에 이미지를 넣고 아래 주석을 해제하세요.
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/hero-dark.png" />
    <source media="(prefers-color-scheme: light)" srcset="docs/screenshots/hero-light.png" />
    <img alt="Seoro 스크린샷" src="docs/screenshots/hero-light.png" width="800" />
  </picture>
  <br>
  <sub>채팅 뷰 / Chat View</sub>
  -->

</div>

<!-- ────────────────────── 주요 기능 ────────────────────── -->

## 주요 기능 <sub>Features</sub>

| | |
|:---|:---|
| 💬 **멀티세션 스트리밍 채팅** | 📊 **분석 대시보드** |
| 여러 Claude 세션을 동시에 운영하고 실시간 스트리밍으로 응답을 받아보세요. 리치 마크다운, 코드 하이라이팅, 이미지 라이트박스, 파일 첨부를 지원합니다. | 활동 히트맵, 비용 개요 및 월간 추정치, 세션 모니터, 스트릭 추적, 시간대별 활동 분포를 한눈에 확인하세요. |
| 🏆 **게이미피케이션** | ⏮ **세션 리플레이** |
| 15단계 레벨 시스템 (**새내기** → **조물주**), 9개 카테고리 105개 업적, 연속 스트릭으로 코딩에 동기를 부여합니다. | 과거 세션을 타임라인으로 재생하며 작업 이력을 탐색하고, 전체 텍스트 검색으로 원하는 대화를 찾아보세요. |
| 🖥 **내장 PTY 터미널** | 🔀 **Git 통합** |
| xterm.js 기반 완전한 터미널 에뮬레이션. 멀티셸 선택, 분할 뷰, 동적 리사이즈를 지원합니다. | 실시간 브랜치 추적, AI 커밋 메시지 자동 생성, 인라인 diff, 워크트리 지원. 머지 상태 실시간 체크(Clean / BehindTarget / ConflictExpected / InConflict), GitHub PR 생성 AI 위임까지. |
| 🧩 **플러그인 시스템** | 📁 **워크스페이스 관리** |
| `~/.claude/plugins` 경로의 커스텀 플러그인을 자동 탐색하고, 권한 시스템으로 안전하게 확장하세요. | 프로젝트별 모델 설정, 시스템 프롬프트, 기본 브랜치, 리뷰 설정을 독립적으로 관리하세요. |
| 📝 **Hooks · Rules · Instructions** | 🧠 **Memory 관리** |
| 커스텀 훅(스크립트, 웹훅, 플러그인), 프로젝트 규칙 파일, 인스트럭션 파일을 GUI에서 직접 관리하세요. | 영구 메모리 항목(User, Feedback, Project, Reference)으로 컨텍스트를 구축하고 워크스페이스별로 필터링하세요. |
| 🔌 **MCP 서버 통합** | 👤 **계정 관리** |
| SSE 및 커맨드 기반 MCP 서버를 전용 페이지에서 설정·관리. Claude Desktop에서 import도 가능합니다. | 여러 Anthropic 계정을 등록하고 즉시 전환하세요. 계정별 사용량을 한눈에 확인할 수 있습니다. |
| 🔄 **자동 업데이트** | |
| Velopack 기반 델타 업데이트로 빠르고 가벼운 업데이트. 인앱 릴리스 노트 뷰어도 포함. | |

<br>

<!-- ────────────────────── 게이미피케이션 ────────────────────── -->

<details>
<summary><b>🏅 레벨 시스템 <sub>Level System</sub></b> — 새내기에서 조물주까지</summary>

<br>

Seoro는 사용량에 따라 XP를 부여하고, 15단계 레벨로 성장을 추적합니다.

| 레벨 | 칭호 | 필요 XP | | 레벨 | 칭호 | 필요 XP |
|:---:|:---|---:|:---:|:---:|:---|---:|
| 1 | 새내기 | 0 | | 9 | 현자 | 6,000 |
| 2 | 탐험가 | 100 | | 10 | 초월자 | 10,000 |
| 3 | 건축가 | 300 | | 11 | 전설 | 15,000 |
| 4 | 설계자 | 600 | | 12 | 신화 | 22,000 |
| 5 | 전문가 | 1,000 | | 13 | 불멸자 | 32,000 |
| 6 | 숙련자 | 1,500 | | 14 | 태초의 자 | 50,000 |
| 7 | 달인 | 2,500 | | 15 | 조물주 | 80,000 |
| 8 | 거장 | 4,000 | | | | |

**업적 등급** &nbsp; `Common` · `Rare` · `Epic` · `Legendary`
&emsp;9개 카테고리 105개 업적: Config · Usage · Streak · Mastery · Explorer · Efficiency · Time · Economy · Pattern

</details>

<br>

<!-- ────────────────────── 기술 스택 ────────────────────── -->

## 기술 스택 <sub>Tech Stack</sub>

| 레이어 | 기술 |
|:---|:---|
| **런타임** | ![.NET](https://img.shields.io/badge/.NET_10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white) |
| **UI 프레임워크** | ![Blazor](https://img.shields.io/badge/Blazor-512BD4?style=flat-square&logo=blazor&logoColor=white) &nbsp;+&nbsp; Photino.Blazor |
| **컴포넌트 라이브러리** | ![MudBlazor](https://img.shields.io/badge/MudBlazor_v9-594AE2?style=flat-square) |
| **코드 에디터** | CodeMirror 6 (Node.js 20 빌드) |
| **마크다운 렌더링** | Markdig |
| **터미널 에뮬레이션** | xterm.js &nbsp;(PTY) |
| **로깅** | Serilog |
| **자동 업데이트** | Velopack |
| **테스트** | xUnit |

<br>

<!-- ────────────────────── 시작하기 ────────────────────── -->

## 시작하기 <sub>Getting Started</sub>

### 필수 요구사항

| | 최소 버전 |
|:---|:---|
| [.NET SDK](https://dotnet.microsoft.com/download) | <kbd>10.0</kbd> |
| [Claude CLI](https://docs.anthropic.com/en/docs/claude-code) | <kbd>≥ 2.1.81</kbd> |

### 릴리스 다운로드 <sub>Download</sub>

[**GitHub Releases**](https://github.com/JinoPay/Seoro/releases/latest) 페이지에서 플랫폼에 맞는 설치 파일을 다운로드하세요.

| 플랫폼 | 아키텍처 | 파일 형식 |
|:---|:---|:---|
| Windows | x64 | `.exe` 설치 파일 |
| macOS | ARM64 (Apple Silicon) | `.app` 번들 |

설치 후 자동 업데이트가 활성화되어 새 버전이 나오면 델타 업데이트로 자동 적용됩니다.

<details>
<summary><b>🔧 소스에서 빌드 <sub>Build from Source</sub></b></summary>

<br>

```bash
# 저장소 클론
git clone https://github.com/JinoPay/Seoro.git
cd Seoro

# CLI 도구 복원
dotnet tool restore

# 빌드
dotnet build Seoro.slnx

# 테스트
dotnet test

# 실행
dotnet run --project src/Seoro.Desktop
```

</details>

<br>

<!-- ────────────────────── 프로젝트 구조 ────────────────────── -->

<details>
<summary><b>📂 프로젝트 구조 <sub>Project Structure</sub></b></summary>

<br>

```
Seoro.slnx                    # .NET XML 솔루션 파일
src/
  Seoro.Desktop/               # 데스크톱 앱 진입점
    Program.cs                    #   DI 컨테이너 (~70개 서비스 등록), Photino 윈도우 초기화
    Services/                     #   플랫폼별 서비스 10개 (파일 피커, 알림, 업데이트 등)
  Seoro.Shared/                # 공유 라이브러리 (플랫폼 독립, ~62,600 lines)
    Models/                       #   데이터 모델 43개 + ViewModels/ 4개
    Services/                     #   비즈니스 로직 135개 파일 (17개 하위 폴더)
      Cli/                          #     CLI 프로바이더 추상화 (Claude, Codex 등)
      Claude/ Codex/                #     CLI별 서비스
      Chat/ Sessions/ Git/          #     채팅, 세션, Git 통합
      Settings/ Knowledge/ Account/ #     설정, 지식, 계정
      Plugin/ Gamification/         #     플러그인, 게이미피케이션
      Infrastructure/ Migration/    #     인프라, 마이그레이션
      Platform/ Notification/       #     플랫폼 인터페이스, 알림
    Components/                   #   Blazor 컴포넌트 100개 (18개 폴더)
    SeoroConstants.cs          #   공유 상수 및 제한값
tests/
  Seoro.Shared.Tests/          # 유닛 테스트 (xUnit)
```

</details>

<!-- ────────────────────── 빌드 & 릴리스 ────────────────────── -->

<details>
<summary><b>🚀 빌드 & 릴리스 <sub>Build &amp; Release</sub></b></summary>

<br>

릴리스는 GitHub Actions로 자동화되어 있습니다. 버전 태그를 push하면 CI/CD가 트리거됩니다.

```bash
git tag v1.17.9
git push origin v1.17.9
```

**CI 파이프라인이 수행하는 작업:**

1. .NET 10 + Node.js 20 설정
2. Windows (x64) 및 macOS (ARM64) 셀프 컨테인드 바이너리 빌드
3. 이전 릴리스와의 델타 업데이트 패키지 생성 (Velopack)
4. GitHub Release에 자동 업로드 및 non-draft 전환

> **Tip:** 태그 push 전에 `changelog.json`에 해당 버전의 변경 내역을 추가하세요.

</details>

<!-- ────────────────────── 단축키 ────────────────────── -->

<details>
<summary><b>⌨️ 주요 단축키 <sub>Keyboard Shortcuts</sub></b></summary>

<br>

| 단축키 | 기능 |
|:---|:---|
| <kbd>Ctrl</kbd>+<kbd>N</kbd> / <kbd>⌘</kbd>+<kbd>N</kbd> | 새 세션 |
| <kbd>Ctrl</kbd>+<kbd>D</kbd> / <kbd>⌘</kbd>+<kbd>D</kbd> | 세션 삭제 |
| <kbd>Ctrl</kbd>+<kbd>1~9</kbd> | 세션 전환 |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>T</kbd> | 테마 전환 |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>E</kbd> | 워크스페이스 전체 펼치기/접기 |
| <kbd>Ctrl</kbd>+<kbd>Enter</kbd> | 메시지 전송 |

</details>

<br>

<!-- ────────────────────── 푸터 ────────────────────── -->

<div align="center">
  <sub>
    Made with ❤️ by <a href="https://github.com/JinoPay">JinoPay</a>
    <br>
    Copyright © 2026 Seoro. All rights reserved.
  </sub>
</div>
