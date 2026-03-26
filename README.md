# Cominomi

Claude Code Desktop GUI Client - Windows & macOS

## Overview

Cominomi is a cross-platform desktop application that provides a graphical interface for [Claude Code](https://docs.anthropic.com/en/docs/claude-code). Built with .NET and Blazor, it offers a rich chat UI with integrated git workflows, workspace management, and plugin support.

## Features

- **Multi-session chat** - Run multiple Claude sessions with streaming responses
- **Workspace management** - Organize projects with workspace-level settings and system prompts
- **Git integration** - Branch management, diff viewing, worktree support
- **Plugin system** - Extend functionality with custom plugins
- **Memory & context** - Persistent memory entries and context building for system prompts
- **Hooks** - Custom actions triggered by events
- **Auto-update** - Automatic delta updates via Velopack
- **Cross-platform** - Windows (x64) and macOS (Apple Silicon)

## Requirements

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Claude CLI](https://docs.anthropic.com/en/docs/claude-code) >= 2.1.81

## Getting Started

```bash
# Clone the repository
git clone https://github.com/JinoPay/Cominomi.git
cd Cominomi

# Restore tools
dotnet tool restore

# Build
dotnet build Cominomi.slnx

# Run
dotnet run --project src/Cominomi.Desktop
```

## Project Structure

```
src/
  Cominomi.Desktop/      # Desktop app (Photino.Blazor window, DI setup)
  Cominomi.Shared/        # Shared library (models, services, UI components)
tests/
  Cominomi.Shared.Tests/  # Unit tests (xUnit)
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 10.0 |
| UI Framework | Blazor + Photino.Blazor |
| Component Library | MudBlazor |
| Markdown | Markdig |
| Logging | Serilog |
| Auto-update | Velopack |
| Testing | xUnit |

## Build & Release

Releases are automated via GitHub Actions. Push a version tag to trigger:

```bash
git tag v1.0.0
git push origin v1.0.0
```

This builds self-contained binaries for Windows (x64) and macOS (ARM64) with delta update support.

## License

All rights reserved.
