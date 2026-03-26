# CLAUDE.md

## Critical Context
- **Tech Stack**: .NET 10.0 + Blazor + Photino.Blazor (desktop) + MudBlazor (UI)
- **Solution**: `Cominomi.slnx` (new XML solution format)
- **Projects**:
  - `src/Cominomi.Desktop` - Desktop app entry point (WinExe, Photino window)
  - `src/Cominomi.Shared` - Shared library (models, services, Razor components)
  - `tests/Cominomi.Shared.Tests` - xUnit test suite
- **Platform**: Windows x64, macOS ARM64
- **Build**: `dotnet build Cominomi.slnx`, release via `dotnet publish` + Velopack
- **Test**: `dotnet test`
- **Auto-update**: Velopack (`vpk` tool)
- **CI/CD**: GitHub Actions (`release.yml`) - triggered on `v*` tags
- **Required CLI**: Claude CLI >= 2.1.81

## Architecture Overview
Cominomi is a cross-platform desktop GUI client for Claude Code (Anthropic's CLI).
It wraps the Claude CLI process and provides a rich Blazor-based UI for chat sessions,
git operations, file management, plugins, and more.

```
src/
  Cominomi.Desktop/         # Entry point, DI setup, platform services
    Program.cs               # Main entry - DI container, Photino window init
    Services/                # Desktop-specific: file picker, notifications, updates
  Cominomi.Shared/           # Core logic (platform-agnostic)
    Models/                  # Data models (Session, ChatMessage, StreamEvent, etc.)
    Services/                # Business logic (ClaudeService, GitService, ChatState, etc.)
    Components/              # Blazor Razor components (Chat/, Settings/, Sidebar/, etc.)
    CominomiConstants.cs     # Shared constants and limits
tests/
  Cominomi.Shared.Tests/    # Unit tests (xUnit)
```

## Key Services
- `ClaudeService` - Manages Claude CLI process, streaming JSON events
- `GitService` - Git operations (clone, diff, branches) with caching
- `ChatState` - UI state management (messages, streaming, tabs)
- `SessionService` - Session CRUD and persistence
- `WorkspaceService` - Workspace (git repo) management
- `StreamEventProcessor` - Handles Claude API streaming event pipeline

## Key Constants (`CominomiConstants.cs`)
- Max 20 active sessions per workspace
- Token limits: context 5K, memory entry 1K, memory total 2.5K, system prompt 10K
- Default permission mode: `bypassAll`
- Branch prefix: `cominomi/`

## Development Workflow
1. `dotnet tool restore` - Restore CLI tools (ilspycmd, vpk)
2. `dotnet build Cominomi.slnx` - Build all projects
3. `dotnet test` - Run tests
4. `dotnet run --project src/Cominomi.Desktop` - Run the app
5. Release: tag `vX.Y.Z` and push to trigger CI/CD

## Anti-Patterns
- Do not modify `CominomiConstants.cs` limits without understanding token budget implications
- Do not bypass the `StreamEventProcessor` pipeline - add new handlers instead
- Do not use blocking I/O in Blazor components - use async throughout
- Do not hard-code platform paths - use the service abstractions
