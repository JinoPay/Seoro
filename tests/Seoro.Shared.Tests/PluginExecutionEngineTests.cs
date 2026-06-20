using System.Text.Json;
using Seoro.Shared.Models;
using Seoro.Shared.Models.Plugin;
using Seoro.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Seoro.Shared.Tests;

public class PluginExecutionEngineTests
{
    private readonly FakePluginService _pluginService = new();
    private readonly FakeProcessRunner _processRunner = new();
    private readonly FakeShellService _shellService = new();
    private readonly FakeHooksEngine _hooksEngine = new();
    private readonly FakeSkillRegistry _skillRegistry = new();

    private PluginExecutionEngine CreateEngine() => new(
        _pluginService,
        _processRunner,
        _shellService,
        _hooksEngine,
        _skillRegistry,
        NullLogger<PluginExecutionEngine>.Instance);

    [Fact]
    public async Task LoadPluginAsync_ValidPlugin_SetsStatusToLoaded()
    {
        var engine = CreateEngine();
        var plugin = MakePlugin("test-plugin", PluginStatus.Valid);

        var result = await engine.LoadPluginAsync(plugin);

        Assert.True(result);
        Assert.Equal(PluginStatus.Loaded, plugin.Status);
        Assert.Contains("test-plugin", engine.LoadedPluginIds);
    }

    [Fact]
    public async Task LoadPluginAsync_InvalidPlugin_ReturnsFalse()
    {
        var engine = CreateEngine();
        var plugin = MakePlugin("bad", PluginStatus.Invalid);

        var result = await engine.LoadPluginAsync(plugin);

        Assert.False(result);
        Assert.DoesNotContain("bad", engine.LoadedPluginIds);
    }

    [Fact]
    public async Task LoadPluginAsync_AlreadyLoaded_ReturnsTrue()
    {
        var engine = CreateEngine();
        var plugin = MakePlugin("dup", PluginStatus.Valid);

        await engine.LoadPluginAsync(plugin);
        var result = await engine.LoadPluginAsync(plugin);

        Assert.True(result);
    }

    [Fact]
    public async Task UnloadPluginAsync_LoadedPlugin_RemovesFromLoaded()
    {
        var engine = CreateEngine();
        var plugin = MakePlugin("rm-me", PluginStatus.Valid);
        await engine.LoadPluginAsync(plugin);

        await engine.UnloadPluginAsync("rm-me");

        Assert.DoesNotContain("rm-me", engine.LoadedPluginIds);
        Assert.Equal(PluginStatus.Valid, plugin.Status);
    }

    [Fact]
    public async Task UnloadPluginAsync_NotLoaded_NoOp()
    {
        var engine = CreateEngine();
        await engine.UnloadPluginAsync("nonexistent"); // should not throw
    }

    [Fact]
    public async Task ExecuteAsync_NotLoaded_ReturnsError()
    {
        var engine = CreateEngine();

        var result = await engine.ExecuteAsync("nope");

        Assert.False(result.Success);
        Assert.Contains("not loaded", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_NoEntryPoint_ReturnsError()
    {
        var engine = CreateEngine();
        var plugin = MakePlugin("no-entry", PluginStatus.Valid, entryPoint: null);
        await engine.LoadPluginAsync(plugin);

        var result = await engine.ExecuteAsync("no-entry");

        Assert.False(result.Success);
        Assert.Contains("no entry point", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_WithEntryPoint_RunsProcess()
    {
        _processRunner.NextResult = new ProcessResult(true, "hello", "", 0);

        var engine = CreateEngine();
        var plugin = MakePlugin("runner", PluginStatus.Valid, entryPoint: "main.js");
        await engine.LoadPluginAsync(plugin);

        var result = await engine.ExecuteAsync("runner");

        Assert.True(result.Success);
        Assert.Equal("hello", result.Output);
        Assert.NotNull(_processRunner.LastOptions);
        Assert.Contains("-c", _processRunner.LastOptions!.Arguments);
    }

    [Fact]
    public async Task ExecuteAsync_PassesEnvironmentVariables()
    {
        _processRunner.NextResult = new ProcessResult(true, "", "", 0);

        var engine = CreateEngine();
        var plugin = MakePlugin("env-test", PluginStatus.Valid, entryPoint: "run.py");
        await engine.LoadPluginAsync(plugin);

        var ctx = new PluginExecutionContext
        {
            Action = "custom-action",
            Parameters = new() { ["foo"] = "bar" }
        };
        await engine.ExecuteAsync("env-test", ctx);

        var env = _processRunner.LastOptions!.EnvironmentVariables!;
        Assert.Equal("env-test", env["SEORO_PLUGIN_ID"]);
        Assert.Equal("custom-action", env["SEORO_PLUGIN_ACTION"]);
        Assert.Equal("bar", env["SEORO_PARAM_FOO"]);
        Assert.Equal("json/1", env["SEORO_PROTOCOL"]);
    }

    [Fact]
    public async Task ExecuteAsync_SendsJsonRequestViaStdin()
    {
        _processRunner.NextResult = new ProcessResult(true, "ok", "", 0);

        var engine = CreateEngine();
        var plugin = MakePlugin("stdin-test", PluginStatus.Valid, entryPoint: "main.js");
        await engine.LoadPluginAsync(plugin);

        var ctx = new PluginExecutionContext
        {
            Action = "transform",
            Parameters = new() { ["input"] = "hello" }
        };
        await engine.ExecuteAsync("stdin-test", ctx);

        var stdin = _processRunner.LastOptions!.StandardInput;
        Assert.NotNull(stdin);

        using var doc = JsonDocument.Parse(stdin);
        var root = doc.RootElement;
        Assert.Equal("stdin-test", root.GetProperty("pluginId").GetString());
        Assert.Equal("transform", root.GetProperty("action").GetString());
        Assert.Equal("hello", root.GetProperty("parameters").GetProperty("input").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_ParsesJsonResponse()
    {
        var jsonResponse = """{"success": true, "message": "done", "data": {"count": 42}}""";
        _processRunner.NextResult = new ProcessResult(true, jsonResponse, "", 0);

        var engine = CreateEngine();
        var plugin = MakePlugin("json-resp", PluginStatus.Valid, entryPoint: "main.js");
        await engine.LoadPluginAsync(plugin);

        var result = await engine.ExecuteAsync("json-resp");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(result.Data!.Success);
        Assert.Equal("done", result.Data.Message);
        Assert.Equal(42, result.Data.Data!.Value.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_PlainTextStdout_DataIsNull()
    {
        _processRunner.NextResult = new ProcessResult(true, "just plain text", "", 0);

        var engine = CreateEngine();
        var plugin = MakePlugin("plain-out", PluginStatus.Valid, entryPoint: "main.sh");
        await engine.LoadPluginAsync(plugin);

        var result = await engine.ExecuteAsync("plain-out");

        Assert.True(result.Success);
        Assert.Equal("just plain text", result.Output);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJsonStdout_DataIsNull()
    {
        _processRunner.NextResult = new ProcessResult(true, "{invalid json", "", 0);

        var engine = CreateEngine();
        var plugin = MakePlugin("bad-json", PluginStatus.Valid, entryPoint: "main.js");
        await engine.LoadPluginAsync(plugin);

        var result = await engine.ExecuteAsync("bad-json");

        Assert.True(result.Success);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task LoadAllAsync_LoadsEnabledValidPlugins()
    {
        _pluginService.Plugins =
        [
            MakePlugin("a", PluginStatus.Valid, isEnabled: true),
            MakePlugin("b", PluginStatus.Invalid, isEnabled: true),
            MakePlugin("c", PluginStatus.Valid, isEnabled: false),
        ];

        var engine = CreateEngine();
        await engine.LoadAllAsync();

        Assert.Single(engine.LoadedPluginIds);
        Assert.Contains("a", engine.LoadedPluginIds);
    }

    [Fact]
    public async Task LoadPluginAsync_WithManifestHooks_RegistersHooks()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), $"plugin-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pluginDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(pluginDir, "manifest.json"), """
            {
                "name": "Hook Plugin",
                "entryPoint": "main.js",
                "hooks": [
                    { "event": "OnMessageComplete", "command": "node {entryPoint} hook" }
                ]
            }
            """);
            await File.WriteAllTextAsync(Path.Combine(pluginDir, "main.js"), "// stub");

            var plugin = new PluginInfo
            {
                Id = "hook-plugin",
                Name = "Hook Plugin",
                Path = pluginDir,
                EntryPoint = "main.js",
                IsEnabled = true,
                Status = PluginStatus.Valid
            };

            var engine = CreateEngine();
            await engine.LoadPluginAsync(plugin);

            Assert.Single(_hooksEngine.AddedHooks);
            Assert.Equal(HookEvent.OnMessageComplete, _hooksEngine.AddedHooks[0].Event);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Fact]
    public async Task LoadPluginAsync_WithManifestSkills_RegistersSkills()
    {
        var pluginDir = Path.Combine(Path.GetTempPath(), $"plugin-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pluginDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(pluginDir, "manifest.json"), """
            {
                "name": "Skill Plugin",
                "entryPoint": "main.js",
                "skills": [
                    { "name": "greet", "description": "Say hello", "command": "node {entryPoint} greet" }
                ]
            }
            """);
            await File.WriteAllTextAsync(Path.Combine(pluginDir, "main.js"), "// stub");

            var plugin = new PluginInfo
            {
                Id = "skill-plugin",
                Name = "Skill Plugin",
                Path = pluginDir,
                EntryPoint = "main.js",
                IsEnabled = true,
                Status = PluginStatus.Valid
            };

            var engine = CreateEngine();
            await engine.LoadPluginAsync(plugin);

            Assert.Single(_skillRegistry.RegisteredSkills);
            Assert.Equal("greet", _skillRegistry.RegisteredSkills[0].Name);
            Assert.Equal("plugin", _skillRegistry.RegisteredSkills[0].Scope);
        }
        finally
        {
            Directory.Delete(pluginDir, true);
        }
    }

    [Theory]
    [InlineData("echo hello", ShellType.Bash, "echo hello")]
    [InlineData("echo \"hello world\"", ShellType.Bash, "echo \\\"hello world\\\"")]
    [InlineData("echo $HOME", ShellType.Bash, "echo \\$HOME")]
    [InlineData("echo `date`", ShellType.Bash, "echo \\`date\\`")]
    [InlineData("echo C:\\path", ShellType.Bash, "echo C:\\\\path")]
    [InlineData("echo hello", ShellType.Cmd, "echo hello")]
    [InlineData("echo foo&bar", ShellType.Cmd, "echo foo^&bar")]
    [InlineData("echo foo|bar", ShellType.Cmd, "echo foo^|bar")]
    [InlineData("echo 50%", ShellType.Cmd, "echo 50%%")]
    public void EscapeShellCommand_EscapesCorrectly(string input, ShellType shellType, string expected)
    {
        var result = PluginExecutionEngine.EscapeShellCommand(input, shellType);
        Assert.Equal(expected, result);
    }

    // --- Helpers ---

    private static PluginInfo MakePlugin(string id, PluginStatus status,
        bool isEnabled = true, string? entryPoint = "main.js")
    {
        return new PluginInfo
        {
            Id = id,
            Name = id,
            Path = "/tmp/plugins/" + id,
            EntryPoint = entryPoint,
            IsEnabled = isEnabled,
            Status = status
        };
    }

    // --- Fakes ---

    private class FakePluginService : IPluginService
    {
        public List<PluginInfo> Plugins { get; set; } = [];
        public string PluginsDirectory => "/tmp/plugins";
        public Task<List<PluginInfo>> GetInstalledPluginsAsync() => Task.FromResult(Plugins);
        public Task<PluginInfo?> GetPluginAsync(string pluginId) =>
            Task.FromResult(Plugins.FirstOrDefault(p => p.Id == pluginId));
        public Task SetPluginEnabledAsync(string pluginId, bool enabled) => Task.CompletedTask;
        public Task<bool> ValidatePluginAsync(string pluginId) => Task.FromResult(true);
        public Task<bool> RemovePluginAsync(string pluginId) => Task.FromResult(true);
        public Task EnsurePluginsDirectoryAsync() => Task.CompletedTask;
        public Task<bool> LoadPluginAsync(string pluginId) => Task.FromResult(true);
        public Task UnloadPluginAsync(string pluginId) => Task.CompletedTask;
        public Task<InstalledPluginsFile> GetInstalledPluginsFileAsync() =>
            Task.FromResult(new InstalledPluginsFile());
        public Task<List<BlockedPlugin>> GetBlockedPluginsAsync() =>
            Task.FromResult(new List<BlockedPlugin>());
        public Task<InstallCountsCache> GetInstallCountsCacheAsync() =>
            Task.FromResult(new InstallCountsCache());
        public Task<List<CliPluginEntry>> ListMarketplacePluginsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<CliPluginEntry>());
        public Task<(bool Success, string Output)> InstallMarketplacePluginAsync(
            string pluginName, CancellationToken ct = default) =>
            Task.FromResult((true, ""));
        public Task<(bool Success, string Output)> UninstallMarketplacePluginAsync(
            string pluginName, CancellationToken ct = default) =>
            Task.FromResult((true, ""));
        public Task<(bool Success, string Output)> EnableMarketplacePluginAsync(
            string pluginName, CancellationToken ct = default) =>
            Task.FromResult((true, ""));
        public Task<(bool Success, string Output)> DisableMarketplacePluginAsync(
            string pluginName, CancellationToken ct = default) =>
            Task.FromResult((true, ""));
        public Task<(bool Success, string Output)> UpdateMarketplacePluginAsync(
            string pluginName, CancellationToken ct = default) =>
            Task.FromResult((true, ""));
    }

    private class FakeProcessRunner : IProcessRunner
    {
        public ProcessResult NextResult { get; set; } = new(true, "", "", 0);
        public ProcessRunOptions? LastOptions { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken ct = default)
        {
            LastOptions = options;
            return Task.FromResult(NextResult);
        }

        public Task<StreamingProcess> RunStreamingAsync(ProcessRunOptions options, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }

    private class FakeShellService : IShellService
    {
        public Task<ShellInfo> GetShellAsync() =>
            Task.FromResult(new ShellInfo("/bin/bash", "-c", ShellType.Bash));
        public Task<ShellInfo> GetTerminalShellAsync() =>
            GetShellAsync();
        public Task<List<ShellInfo>> GetAvailableShellsAsync() =>
            Task.FromResult(new List<ShellInfo> { new("/bin/bash", "-c", ShellType.Bash) });
        public Task<string?> WhichAsync(string executableName) =>
            Task.FromResult<string?>(null);
        public Task<string?> GetLoginShellPathAsync() =>
            Task.FromResult<string?>(null);
        public void InvalidateCache() { }
    }

    private class FakeHooksEngine : IHooksEngine
    {
        public List<HookDefinition> AddedHooks { get; } = [];

        public Task<List<HookExecutionResult>> FireAsync(HookEvent hookEvent, Dictionary<string, string>? env = null) =>
            Task.FromResult<List<HookExecutionResult>>([]);
        public List<HookDefinition> GetHooks() => AddedHooks;
        public Task AddHookAsync(HookDefinition hook) { AddedHooks.Add(hook); return Task.CompletedTask; }
        public Task RemoveHookAsync(HookEvent hookEvent, string command)
        {
            AddedHooks.RemoveAll(h => h.Event == hookEvent && h.Command == command);
            return Task.CompletedTask;
        }
        public Task LoadAsync() => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
    }

    private class FakeSkillRegistry : ISkillRegistry
    {
        public List<SkillDefinition> RegisteredSkills { get; } = [];
        private readonly List<(string name, string scope)> _deleted = [];

        public IReadOnlyList<SkillDefinition> GetAll() => RegisteredSkills;
        public SkillDefinition? Find(string name) => RegisteredSkills.FirstOrDefault(s => s.Name == name);
        public string? TryParseSkillCommand(string input, out string? args) { args = null; return null; }
        public string ExpandSkill(SkillDefinition skill, string? args, Session session) => skill.PromptTemplate;
        public bool TryParseSkillChain(string input, Session session, out List<SkillChainStep> steps) { steps = []; return false; }
        public void Register(SkillDefinition skill) => RegisteredSkills.Add(skill);
        public Task LoadCustomCommandsAsync(string? projectPath) => Task.CompletedTask;
        public Task SaveCommandAsync(SkillDefinition command, string? projectPath = null) => Task.CompletedTask;
        public Task DeleteCommandAsync(string name, string scope, string? projectPath)
        {
            _deleted.Add((name, scope));
            RegisteredSkills.RemoveAll(s => s.Name == name && s.Scope == scope);
            return Task.CompletedTask;
        }
    }
}
