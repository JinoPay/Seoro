using System.Text.Json.Serialization;

namespace Seoro.Shared.Models.Plugin;

/// <summary>
///     ~/.claude/plugins/installed_plugins.json
///     { "version": 2, "plugins": { "name@marketplace": [ { "scope", "version", "installedAt" } ] } }
/// </summary>
public class InstalledPluginsFile
{
    [JsonPropertyName("version")] public int Version { get; set; }

    [JsonPropertyName("plugins")]
    public Dictionary<string, List<InstallEntry>> Plugins { get; set; } = new();
}

public class InstallEntry
{
    [JsonPropertyName("scope")] public string Scope { get; set; } = "user";
    [JsonPropertyName("version")] public string Version { get; set; } = "unknown";
    [JsonPropertyName("installedAt")] public string InstalledAt { get; set; } = "";
}

/// <summary>
///     ~/.claude/plugins/blocked_plugins.json  — array of blocked entries
/// </summary>
public class BlockedPlugin
{
    [JsonPropertyName("plugin")] public string Plugin { get; set; } = "";
    [JsonPropertyName("added_at")] public string AddedAt { get; set; } = "";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
}

/// <summary>
///     ~/.claude/plugins/install-counts-cache.json
///     { "counts": [ { "plugin": "name@marketplace", "unique_installs": 123 } ] }
/// </summary>
public class InstallCountsCache
{
    [JsonPropertyName("counts")] public List<PluginInstallCount> Counts { get; set; } = [];
}

public class PluginInstallCount
{
    [JsonPropertyName("plugin")] public string Plugin { get; set; } = "";
    [JsonPropertyName("unique_installs")] public int UniqueInstalls { get; set; }
}

/// <summary>
///     View model: installed_plugins.json의 항목 하나를 UI 카드에 표시하기 위해 flat하게 변환한 모델
/// </summary>
public class MarketplaceInstalledPlugin
{
    public string Name { get; set; } = "";
    public string Marketplace { get; set; } = "";
    public string Scope { get; set; } = "user";
    public string Version { get; set; } = "unknown";
    public string InstalledAt { get; set; } = "";
}
