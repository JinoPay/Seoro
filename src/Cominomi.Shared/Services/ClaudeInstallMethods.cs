namespace Cominomi.Shared.Services;

public static class ClaudeInstallMethods
{
    public static readonly IReadOnlyList<InstallMethod> Windows =
    [
        new("PowerShell", "irm https://claude.ai/install.ps1 | iex",
                           "irm https://claude.ai/install.ps1 | iex"),
        new("winget",     "winget install Anthropic.ClaudeCode",
                           "winget upgrade Anthropic.ClaudeCode"),
        new("npm",        "npm install -g @anthropic-ai/claude-code",
                           "npm update -g @anthropic-ai/claude-code")
    ];

    public static readonly IReadOnlyList<InstallMethod> Mac =
    [
        new("curl",      "curl -fsSL https://claude.ai/install.sh | bash",
                          "curl -fsSL https://claude.ai/install.sh | bash"),
        new("Homebrew",  "brew install --cask claude-code",
                          "brew upgrade --cask claude-code"),
        new("npm",       "npm install -g @anthropic-ai/claude-code",
                          "npm update -g @anthropic-ai/claude-code")
    ];
}
