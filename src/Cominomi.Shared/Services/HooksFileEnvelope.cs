using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

/// <summary>
/// Wrapper for hooks.json that adds $schemaVersion support.
/// Old format: bare array [...]. New format: { "$schemaVersion": 1, "hooks": [...] }.
/// </summary>
public class HooksFileEnvelope
{
    public int SchemaVersion { get; set; } = 1;
    public List<HookDefinition> Hooks { get; set; } = [];
}
