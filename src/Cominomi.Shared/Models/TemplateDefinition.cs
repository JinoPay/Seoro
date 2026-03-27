using System.Text.Json.Serialization;

namespace Cominomi.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TemplateCategory
{
    Skill,
    Agent,
    Rule,
    Hook,
    Mcp
}

public class TemplateDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required TemplateCategory Category { get; init; }

    /// <summary>Content body (skill/agent/rule markdown content).</summary>
    public string? Content { get; init; }

    // Rule-specific
    public List<string>? Paths { get; init; }

    // Hook-specific
    public string? HookEvent { get; init; }
    public string? Matcher { get; init; }
    public string? HookType { get; init; }
    public string? HookValue { get; init; }

    // MCP-specific
    public string? McpType { get; init; }
    public string? McpCommand { get; init; }
    public string? McpArgs { get; init; }
    public string? McpUrl { get; init; }
}

public static class BuiltInTemplates
{
    public static readonly IReadOnlyList<TemplateDefinition> All = BuildAll();

    private static List<TemplateDefinition> BuildAll() =>
    [
        // === Skills ===
        new()
        {
            Id = "skill-code-review", Name = "Code Review",
            Description = "코드 품질, 버그, 모범 사례 검토",
            Category = TemplateCategory.Skill,
            Content = "---\nname: code-review\ndescription: Review code for quality, bugs, and best practices\nuser-invocable: true\nargument-hint: \"[file or PR]\"\n---\n\nReview the specified code for:\n- Logic errors and edge cases\n- Performance issues\n- Security vulnerabilities\n- Code style and readability\n\nProvide actionable feedback with specific line references.\n"
        },
        new()
        {
            Id = "skill-deploy", Name = "Deploy",
            Description = "환경에 애플리케이션 배포",
            Category = TemplateCategory.Skill,
            Content = "---\nname: deploy\ndescription: Deploy the application\ndisable-model-invocation: true\nuser-invocable: true\nargument-hint: \"[environment]\"\nallowed-tools: Bash\n---\n\n## Deploy to $ARGUMENTS\n\n1. Run tests\n2. Build the application\n3. Deploy to $ARGUMENTS environment\n4. Verify deployment\n"
        },
        new()
        {
            Id = "skill-explain", Name = "Explain Code",
            Description = "코드 구조와 로직 상세 설명",
            Category = TemplateCategory.Skill,
            Content = "---\nname: explain-code\ndescription: Explain code structure and logic\nuser-invocable: true\nargument-hint: \"[file path]\"\ncontext: fork\nagent: Explore\n---\n\nExplain $ARGUMENTS in detail:\n1. What it does\n2. How it works\n3. Key patterns used\n4. Dependencies\n"
        },
        new()
        {
            Id = "skill-test-runner", Name = "Test Runner",
            Description = "테스트 실행 및 결과 분석",
            Category = TemplateCategory.Skill,
            Content = "---\nname: test-runner\ndescription: Run tests and analyze results\nuser-invocable: true\nallowed-tools: Bash, Read\n---\n\nRun the project's test suite:\n1. Identify the test framework\n2. Run all tests\n3. Analyze failures\n4. Suggest fixes\n"
        },
        new()
        {
            Id = "skill-pr-review", Name = "PR Review",
            Description = "풀 리퀘스트 변경 사항 검토",
            Category = TemplateCategory.Skill,
            Content = "---\nname: pr-review\ndescription: Review a pull request\nuser-invocable: true\nargument-hint: \"[PR number]\"\nallowed-tools: Bash, Read, Glob, Grep\n---\n\nReview PR #$ARGUMENTS:\n\n1. Get PR diff: !`gh pr diff $0`\n2. Get PR description: !`gh pr view $0`\n3. Analyze changes for correctness, test coverage, security, performance\n4. Provide summary with approve/request-changes recommendation\n"
        },
        new()
        {
            Id = "skill-refactor", Name = "Refactor",
            Description = "더 나은 구조로 코드 리팩토링",
            Category = TemplateCategory.Skill,
            Content = "---\nname: refactor\ndescription: Refactor code for better structure and readability\nuser-invocable: true\nargument-hint: \"[file or module]\"\n---\n\nRefactor $ARGUMENTS:\n1. Identify code smells\n2. Extract functions/modules\n3. Simplify complex logic\n4. Improve naming\n5. Ensure tests still pass\n"
        },

        // === Agents ===
        new()
        {
            Id = "agent-bug-fixer", Name = "Bug Fixer",
            Description = "버그 조사 및 수정 전문 에이전트",
            Category = TemplateCategory.Agent,
            Content = "---\nname: bug-fixer\ndescription: Investigate and fix reported bugs\nmodel: opus\neffort: high\ntools: Read, Glob, Grep, Edit, Bash\npermissions: acceptEdits\n---\n\nYou are an expert debugger. When given a bug report:\n1. Reproduce the issue by reading relevant code\n2. Identify the root cause\n3. Implement a minimal fix\n4. Verify the fix doesn't break existing tests\n"
        },
        new()
        {
            Id = "agent-security", Name = "Security Auditor",
            Description = "보안 취약점 감사 에이전트",
            Category = TemplateCategory.Agent,
            Content = "---\nname: security-auditor\ndescription: Audit code for security vulnerabilities\nmodel: opus\ntools: Read, Glob, Grep\npermissions: plan\nmemory: project\n---\n\nYou are a security expert. Analyze code for:\n- OWASP Top 10 vulnerabilities\n- Injection attacks (SQL, XSS, command)\n- Authentication/authorization flaws\n- Secrets in code\n- Dependency vulnerabilities\n\nProvide severity ratings and remediation steps.\n"
        },
        new()
        {
            Id = "agent-docs", Name = "Docs Generator",
            Description = "포괄적 문서 생성 에이전트",
            Category = TemplateCategory.Agent,
            Content = "---\nname: docs-generator\ndescription: Generate comprehensive documentation\nmodel: sonnet\ntools: Read, Glob, Grep, Write\n---\n\nYou generate documentation. For each file/module:\n1. Read and understand the code\n2. Generate JSDoc/TSDoc comments\n3. Create README sections\n4. Add usage examples\n"
        },
        new()
        {
            Id = "agent-perf", Name = "Performance Optimizer",
            Description = "성능 분석 및 최적화 에이전트",
            Category = TemplateCategory.Agent,
            Content = "---\nname: performance-optimizer\ndescription: Analyze and optimize code performance\nmodel: opus\ntools: Read, Glob, Grep, Edit, Bash\nmemory: project\n---\n\nYou are a performance expert. When analyzing code:\n1. Profile hot paths\n2. Identify bottlenecks (O(n^2), unnecessary allocations)\n3. Suggest optimizations with benchmarks\n4. Implement changes\n"
        },

        // === Rules ===
        new()
        {
            Id = "rule-ts-strict", Name = "TypeScript Strict",
            Description = "엄격한 TypeScript 패턴 강제",
            Category = TemplateCategory.Rule,
            Paths = ["**/*.ts", "**/*.tsx"],
            Content = "# TypeScript Rules\n\n- Use strict mode, no `any` types\n- Named exports over default exports\n- Prefer interfaces over type aliases for objects\n- Use explicit return types on public functions\n- No unused variables or imports"
        },
        new()
        {
            Id = "rule-api", Name = "API Design",
            Description = "REST API 규칙 및 유효성 검사",
            Category = TemplateCategory.Rule,
            Paths = ["src/api/**/*", "src/routes/**/*"],
            Content = "# API Design Rules\n\n- All endpoints must validate input\n- Use standard error format: `{ error: string, code: number }`\n- Return proper HTTP status codes\n- Include pagination for list endpoints\n- Document endpoints with JSDoc"
        },
        new()
        {
            Id = "rule-testing", Name = "Testing Standards",
            Description = "테스트 명명, 커버리지, 패턴",
            Category = TemplateCategory.Rule,
            Paths = ["**/*.test.*", "**/*.spec.*"],
            Content = "# Testing Rules\n\n- Co-locate test files with source files\n- Use descriptive names: 'should [behavior] when [condition]'\n- No test should depend on another test's state\n- Mock external services, not internal modules\n- Aim for 80%+ coverage on critical paths"
        },
        new()
        {
            Id = "rule-security", Name = "Security",
            Description = "코드 보안 모범 사례",
            Category = TemplateCategory.Rule,
            Content = "# Security Rules\n\n- Never commit secrets, API keys, or credentials\n- Sanitize user input before database queries\n- Use parameterized queries, never string concatenation\n- Validate all external data at system boundaries\n- Use HTTPS for all external API calls"
        },
        new()
        {
            Id = "rule-git", Name = "Git Conventions",
            Description = "커밋 메시지 및 브랜칭 규칙",
            Category = TemplateCategory.Rule,
            Content = "# Git Commit Rules\n\n- Use conventional commits: `type(scope): description`\n- Types: feat, fix, refactor, test, docs, chore\n- Keep commits atomic and focused\n- Write imperative mood: 'add feature' not 'added feature'\n- Reference issue numbers when applicable"
        },
        new()
        {
            Id = "rule-errors", Name = "Error Handling",
            Description = "에러 처리 패턴",
            Category = TemplateCategory.Rule,
            Paths = ["src/**/*"],
            Content = "# Error Handling Rules\n\n- Never swallow errors silently\n- Use custom error classes for domain errors\n- Always include error context\n- Log errors at the point of handling, not catching\n- Return user-friendly messages, log technical details"
        },
        new()
        {
            Id = "rule-a11y", Name = "Accessibility",
            Description = "UI 접근성 표준",
            Category = TemplateCategory.Rule,
            Paths = ["**/*.razor", "**/*.cshtml"],
            Content = "# Accessibility Rules\n\n- All images must have alt text\n- Use semantic HTML elements\n- Ensure keyboard navigation works\n- Maintain sufficient color contrast\n- Add aria-labels to icon-only buttons"
        },
        new()
        {
            Id = "rule-perf", Name = "Performance",
            Description = "성능 최적화 가이드라인",
            Category = TemplateCategory.Rule,
            Content = "# Performance Rules\n\n- Avoid N+1 queries — use batch loading\n- Use pagination for large data sets\n- Lazy-load non-critical resources\n- Profile before optimizing\n- Cache expensive computations"
        },

        // === Hooks ===
        new()
        {
            Id = "hook-bash-validator", Name = "Bash Validator",
            Description = "Bash 명령어 실행 전 유효성 검사",
            Category = TemplateCategory.Hook,
            HookEvent = "PreToolUse", Matcher = "Bash",
            HookType = "command", HookValue = ".claude/hooks/validate.sh"
        },
        new()
        {
            Id = "hook-webhook", Name = "HTTP Webhook",
            Description = "HTTP 엔드포인트로 이벤트 전송",
            Category = TemplateCategory.Hook,
            HookEvent = "PreToolUse",
            HookType = "http", HookValue = "http://localhost:8080/hook"
        },
        new()
        {
            Id = "hook-prompt-guard", Name = "Prompt Guard",
            Description = "AI가 실행 전 액션 검증",
            Category = TemplateCategory.Hook,
            HookEvent = "PreToolUse",
            HookType = "prompt", HookValue = "Check if this action is safe and appropriate"
        },
        new()
        {
            Id = "hook-log-usage", Name = "Log Usage",
            Description = "도구 사용을 파일에 기록",
            Category = TemplateCategory.Hook,
            HookEvent = "PostToolUse",
            HookType = "command", HookValue = "echo \"$(date): tool used\" >> ~/.claude/usage.log"
        },
        new()
        {
            Id = "hook-cleanup", Name = "Session Cleanup",
            Description = "세션 종료 시 정리 작업 실행",
            Category = TemplateCategory.Hook,
            HookEvent = "SessionEnd",
            HookType = "command", HookValue = "echo 'Session ended'"
        },

        // === MCP ===
        new()
        {
            Id = "mcp-filesystem", Name = "Filesystem",
            Description = "MCP를 통한 로컬 파일 읽기/쓰기",
            Category = TemplateCategory.Mcp,
            McpType = "stdio", McpCommand = "npx",
            McpArgs = "-y @modelcontextprotocol/server-filesystem /path/to/dir"
        },
        new()
        {
            Id = "mcp-github", Name = "GitHub",
            Description = "MCP를 통한 GitHub API 접근",
            Category = TemplateCategory.Mcp,
            McpType = "stdio", McpCommand = "npx",
            McpArgs = "-y @modelcontextprotocol/server-github"
        },
        new()
        {
            Id = "mcp-postgres", Name = "PostgreSQL",
            Description = "PostgreSQL 데이터베이스 쿼리",
            Category = TemplateCategory.Mcp,
            McpType = "stdio", McpCommand = "npx",
            McpArgs = "-y @modelcontextprotocol/server-postgres postgresql://localhost/mydb"
        },
        new()
        {
            Id = "mcp-memory", Name = "Memory",
            Description = "MCP를 통한 영구 메모리 저장소",
            Category = TemplateCategory.Mcp,
            McpType = "stdio", McpCommand = "npx",
            McpArgs = "-y @modelcontextprotocol/server-memory"
        },
        new()
        {
            Id = "mcp-sqlite", Name = "SQLite",
            Description = "SQLite 데이터베이스 쿼리",
            Category = TemplateCategory.Mcp,
            McpType = "stdio", McpCommand = "npx",
            McpArgs = "-y @modelcontextprotocol/server-sqlite /path/to/db.sqlite"
        },
    ];
}
