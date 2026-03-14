namespace Cominomi.Shared.Models;

public class CliCapabilities
{
    public string Version { get; set; } = "";
    public bool SupportsVerbose { get; set; }
    public bool RequiresVerboseForStreamJson { get; set; }
}
