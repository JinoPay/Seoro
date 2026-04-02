namespace Cominomi.Shared.Models;

public class CliCapabilities
{
    public bool RequiresVerboseForStreamJson { get; set; }
    public bool SupportsVerbose { get; set; }
    public string Version { get; set; } = "";
}