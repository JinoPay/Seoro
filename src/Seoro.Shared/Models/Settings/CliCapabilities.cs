namespace Seoro.Shared.Models.Settings;

public class CliCapabilities
{
    public bool RequiresVerboseForStreamJson { get; set; }
    public bool SupportsVerbose { get; set; }

    /// <summary>--input-format stream-json(양방향 control protocol) 지원 여부.</summary>
    public bool SupportsBidirectional { get; set; }

    public string Version { get; set; } = "";
}