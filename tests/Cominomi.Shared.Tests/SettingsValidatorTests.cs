using Cominomi.Shared.Models;
using Cominomi.Shared.Services;

namespace Cominomi.Shared.Tests;

public class SettingsValidatorTests
{
    [Fact]
    public void Validate_DefaultSettings_ReturnsNoIssues()
    {
        var settings = new AppSettings();
        var issues = SettingsValidator.Validate(settings);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    [InlineData("system")]
    public void Validate_ValidTheme_NoIssue(string theme)
    {
        var settings = new AppSettings { Theme = theme };
        var issues = SettingsValidator.Validate(settings);
        Assert.DoesNotContain(issues, i => i.Contains("theme", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("purple")]
    [InlineData("")]
    public void Validate_InvalidTheme_ReturnsIssue(string theme)
    {
        var settings = new AppSettings { Theme = theme };
        var issues = SettingsValidator.Validate(settings);
        Assert.Contains(issues, i => i.Contains("theme", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NegativeTimeout_ReturnsIssue()
    {
        var settings = new AppSettings { DefaultProcessTimeoutSeconds = -1 };
        var issues = SettingsValidator.Validate(settings);
        Assert.Contains(issues, i => i.Contains("DefaultProcessTimeoutSeconds"));
    }

    [Fact]
    public void Validate_NegativeBudget_ReturnsIssue()
    {
        var settings = new AppSettings { DefaultMaxBudgetUsd = -5m };
        var issues = SettingsValidator.Validate(settings);
        Assert.Contains(issues, i => i.Contains("DefaultMaxBudgetUsd"));
    }

    [Fact]
    public void Sanitize_InvalidTheme_ClampsToDefault()
    {
        var settings = new AppSettings { Theme = "neon" };
        SettingsValidator.Sanitize(settings);
        Assert.Equal("dark", settings.Theme);
    }

    [Fact]
    public void Sanitize_NegativeTimeout_ClampsToOne()
    {
        var settings = new AppSettings { DefaultProcessTimeoutSeconds = -10, HookTimeoutSeconds = 0 };
        SettingsValidator.Sanitize(settings);
        Assert.Equal(1, settings.DefaultProcessTimeoutSeconds);
        Assert.Equal(1, settings.HookTimeoutSeconds);
    }

    [Fact]
    public void Sanitize_InvalidEffortLevel_ClampsToDefault()
    {
        var settings = new AppSettings { DefaultEffortLevel = "turbo" };
        SettingsValidator.Sanitize(settings);
        Assert.Equal(CominomiConstants.DefaultEffortLevel, settings.DefaultEffortLevel);
    }

    [Fact]
    public void Sanitize_InvalidMergeStrategy_ClampsToDefault()
    {
        var settings = new AppSettings { DefaultMergeStrategy = "yolo" };
        SettingsValidator.Sanitize(settings);
        Assert.Equal(CominomiConstants.DefaultMergeStrategy, settings.DefaultMergeStrategy);
    }

    [Fact]
    public void Sanitize_NegativeMaxBudget_ClearsToNull()
    {
        var settings = new AppSettings { DefaultMaxBudgetUsd = -1m };
        SettingsValidator.Sanitize(settings);
        Assert.Null(settings.DefaultMaxBudgetUsd);
    }

    [Fact]
    public void Sanitize_ZeroMaxTurns_ClearsToNull()
    {
        var settings = new AppSettings { DefaultMaxTurns = 0 };
        SettingsValidator.Sanitize(settings);
        Assert.Null(settings.DefaultMaxTurns);
    }

    [Fact]
    public void SanitizeWorkspace_EnsuresPreferencesNotNull()
    {
        var workspace = new Workspace();
        workspace.Preferences = null!;
        SettingsValidator.SanitizeWorkspace(workspace);
        Assert.NotNull(workspace.Preferences);
    }

    [Fact]
    public void SanitizeWorkspace_EmptyRemote_DefaultsToOrigin()
    {
        var workspace = new Workspace { DefaultRemote = "" };
        SettingsValidator.SanitizeWorkspace(workspace);
        Assert.Equal("origin", workspace.DefaultRemote);
    }
}
