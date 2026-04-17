namespace Seoro.Shared.Models.Settings;

public class CliCapabilities
{
    public bool RequiresVerboseForStreamJson { get; set; }
    public bool SupportsVerbose { get; set; }
    public string Version { get; set; } = "";
    public bool SupportsOpus47 { get; set; }
    public bool SupportsXHighEffort { get; set; }
}