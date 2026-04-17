using System.Globalization;
using System.Resources;

namespace Seoro.Shared.Resources;

/// <summary>
///     Strongly-typed accessor for localized strings from Strings.resx.
///     Default culture is Korean (ko). Add satellite assemblies for other cultures.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Rm =
        new("Seoro.Shared.Resources.Strings", typeof(Strings).Assembly);

    // ── Culture Switching ──

    /// <summary>Fired when the UI culture changes via SetCulture().</summary>
    public static event Action? CultureChanged;

    /// <summary>
    ///     Sets the UI culture and fires CultureChanged so Blazor components can re-render.
    ///     <paramref name="languageCode"/> should be "ko" or "en".
    /// </summary>
    public static void SetCulture(string languageCode)
    {
        var culture = new CultureInfo(languageCode == "en" ? "en" : "ko");
        // 전역 기본값 설정 — 렌더러 스레드 등 모든 스레드에 적용
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        // 현재 호출 스레드에도 즉시 적용
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureChanged?.Invoke();
    }

    // ── Time Formatting Helpers ──

    /// <summary>Formats a relative time span (e.g. "3분 전" / "3m ago").</summary>
    public static string FormatRelativeTime(TimeSpan diff)
    {
        if (diff.TotalSeconds < 60)
            return Common_Time_JustNow;
        if (diff.TotalMinutes < 60)
            return Common_Time_MinutesAgo((int)diff.TotalMinutes);
        if (diff.TotalHours < 24)
            return Common_Time_HoursAgo((int)diff.TotalHours);
        return Common_Time_DaysAgo((int)diff.TotalDays);
    }

    /// <summary>Formats an elapsed duration (e.g. "2분 30초" / "2m 30s").</summary>
    public static string FormatElapsedTime(TimeSpan elapsed)
    {
        if (elapsed.TotalMinutes < 1)
            return Common_Time_Seconds((int)elapsed.TotalSeconds);
        return Common_Time_MinutesSeconds((int)elapsed.TotalMinutes, elapsed.Seconds);
    }

    // ── Common Actions ──
    public static string Common_Save => Get(nameof(Common_Save));
    public static string Common_Cancel => Get(nameof(Common_Cancel));
    public static string Common_Delete => Get(nameof(Common_Delete));
    public static string Common_Confirm => Get(nameof(Common_Confirm));
    public static string Common_Close => Get(nameof(Common_Close));
    public static string Common_Back => Get(nameof(Common_Back));
    public static string Common_Edit => Get(nameof(Common_Edit));
    public static string Common_Add => Get(nameof(Common_Add));
    public static string Common_Search => Get(nameof(Common_Search));
    public static string Common_Copy => Get(nameof(Common_Copy));
    public static string Common_Open => Get(nameof(Common_Open));
    public static string Common_Retry => Get(nameof(Common_Retry));
    public static string Common_Loading => Get(nameof(Common_Loading));
    public static string Common_Error => Get(nameof(Common_Error));
    public static string Common_Settings => Get(nameof(Common_Settings));
    public static string Common_Unknown => Get(nameof(Common_Unknown));
    public static string Common_None => Get(nameof(Common_None));
    public static string Common_Yes => Get(nameof(Common_Yes));
    public static string Common_No => Get(nameof(Common_No));

    // ── Common Status ──
    public static string Common_Status_Error => Get(nameof(Common_Status_Error));
    public static string Common_Status_Archived => Get(nameof(Common_Status_Archived));
    public static string Common_Status_Completed => Get(nameof(Common_Status_Completed));
    public static string Common_Status_Synced => Get(nameof(Common_Status_Synced));
    public static string Common_Status_Local => Get(nameof(Common_Status_Local));

    // ── Common Time ──
    public static string Common_Time_JustNow => Get(nameof(Common_Time_JustNow));
    public static string Common_Time_DateFormat(int year, int month, int day) => Get(nameof(Common_Time_DateFormat), year, month, day);
    public static string Common_Time_SecondsAgo(int seconds) => Get(nameof(Common_Time_SecondsAgo), seconds);
    public static string Common_Time_MinutesAgo(int minutes) => Get(nameof(Common_Time_MinutesAgo), minutes);
    public static string Common_Time_HoursAgo(int hours) => Get(nameof(Common_Time_HoursAgo), hours);
    public static string Common_Time_DaysAgo(int days) => Get(nameof(Common_Time_DaysAgo), days);
    public static string Common_Time_Seconds(int seconds) => Get(nameof(Common_Time_Seconds), seconds);
    public static string Common_Time_MinutesSeconds(int minutes, int seconds) => Get(nameof(Common_Time_MinutesSeconds), minutes, seconds);

    // ── Settings Navigation ──
    public static string Nav_BackToApp => Get(nameof(Nav_BackToApp));
    public static string Nav_Dashboard => Get(nameof(Nav_Dashboard));
    public static string Nav_Usage => Get(nameof(Nav_Usage));
    public static string Nav_AppSettings => Get(nameof(Nav_AppSettings));
    public static string Nav_General => Get(nameof(Nav_General));
    public static string Nav_AiModel => Get(nameof(Nav_AiModel));
    public static string Nav_Notifications => Get(nameof(Nav_Notifications));
    public static string Nav_Terminal => Get(nameof(Nav_Terminal));
    public static string Nav_GitMerge => Get(nameof(Nav_GitMerge));
    public static string Nav_Shortcuts => Get(nameof(Nav_Shortcuts));
    public static string Nav_IntegrationsAndExtensions => Get(nameof(Nav_IntegrationsAndExtensions));
    public static string Nav_Integrations => Get(nameof(Nav_Integrations));
    public static string Nav_SlashCommands => Get(nameof(Nav_SlashCommands));
    public static string Nav_ClaudeSettings => Get(nameof(Nav_ClaudeSettings));
    public static string Nav_McpServers => Get(nameof(Nav_McpServers));
    public static string Nav_Hooks => Get(nameof(Nav_Hooks));
    public static string Nav_Rules => Get(nameof(Nav_Rules));
    public static string Nav_Instructions => Get(nameof(Nav_Instructions));
    public static string Nav_Plugins => Get(nameof(Nav_Plugins));
    public static string Nav_Accounts => Get(nameof(Nav_Accounts));
    public static string Nav_Tools => Get(nameof(Nav_Tools));
    public static string Nav_SessionHistory => Get(nameof(Nav_SessionHistory));
    public static string Nav_Memory => Get(nameof(Nav_Memory));
    public static string Nav_System => Get(nameof(Nav_System));
    public static string Nav_Advanced => Get(nameof(Nav_Advanced));
    public static string Nav_Updates => Get(nameof(Nav_Updates));
    public static string Nav_Workspaces => Get(nameof(Nav_Workspaces));

    // ── Settings: General Section ──
    public static string Settings_General_Title => Get(nameof(Settings_General_Title));
    public static string Settings_General_Desc => Get(nameof(Settings_General_Desc));
    public static string Settings_General_ThemeLabel => Get(nameof(Settings_General_ThemeLabel));
    public static string Settings_General_ThemeDark => Get(nameof(Settings_General_ThemeDark));
    public static string Settings_General_ThemeLight => Get(nameof(Settings_General_ThemeLight));
    public static string Settings_General_ThemeSystem => Get(nameof(Settings_General_ThemeSystem));
    public static string Settings_General_UiLanguageLabel => Get(nameof(Settings_General_UiLanguageLabel));
    public static string Settings_General_UiScaleTitle => Get(nameof(Settings_General_UiScaleTitle));
    public static string Settings_General_UiScaleAuto => Get(nameof(Settings_General_UiScaleAuto));
    public static string Settings_General_UiScaleHint => Get(nameof(Settings_General_UiScaleHint));
    public static string Settings_General_GitWorkflowTitle => Get(nameof(Settings_General_GitWorkflowTitle));
    public static string Settings_General_CloneDirLabel => Get(nameof(Settings_General_CloneDirLabel));
    public static string Settings_General_CloneDirPlaceholder => Get(nameof(Settings_General_CloneDirPlaceholder));
    public static string Settings_General_CloneDirHint => Get(nameof(Settings_General_CloneDirHint));
    public static string Settings_General_BrowseDir => Get(nameof(Settings_General_BrowseDir));
    public static string Settings_General_SessionLangLabel => Get(nameof(Settings_General_SessionLangLabel));
    public static string Settings_General_SessionLangHint => Get(nameof(Settings_General_SessionLangHint));
    public static string Settings_General_HelpTitle => Get(nameof(Settings_General_HelpTitle));
    public static string Settings_General_ShowTutorial => Get(nameof(Settings_General_ShowTutorial));
    public static string Settings_General_ShowWhatsNew => Get(nameof(Settings_General_ShowWhatsNew));
    public static string Settings_General_MigrationTitle => Get(nameof(Settings_General_MigrationTitle));
    public static string Settings_General_MigrationDesc => Get(nameof(Settings_General_MigrationDesc));
    public static string Settings_General_MigrationFoundData(int sessions, int workspaces, int memories, int tasks, int configs) => string.Format(Get(nameof(Settings_General_MigrationFoundData)), sessions, workspaces, memories, tasks, configs);
    public static string Settings_General_MigrationImporting => Get(nameof(Settings_General_MigrationImporting));
    public static string Settings_General_MigrationImport => Get(nameof(Settings_General_MigrationImport));
    public static string Settings_General_ImportDialogTitle => Get(nameof(Settings_General_ImportDialogTitle));
    public static string Settings_General_ImportDialogMsg(int count) => Get(nameof(Settings_General_ImportDialogMsg), count);
    public static string Settings_General_ImportOverwrite => Get(nameof(Settings_General_ImportOverwrite));
    public static string Settings_General_ImportKeep => Get(nameof(Settings_General_ImportKeep));
    public static string Settings_General_ImportDoneTitle => Get(nameof(Settings_General_ImportDoneTitle));
    public static string Settings_General_ImportDoneMsg(int count) => Get(nameof(Settings_General_ImportDoneMsg), count);
    public static string Settings_General_ImportResult(int skipped) => Get(nameof(Settings_General_ImportResult), skipped);
    public static string Settings_General_ImportFailed(string error) => Get(nameof(Settings_General_ImportFailed), error);
    public static string Settings_General_ImportFailedCount(int count) => Get(nameof(Settings_General_ImportFailedCount), count);
    public static string Settings_General_ImportFolderDeleteFailed => Get(nameof(Settings_General_ImportFolderDeleteFailed));

    // ── Settings: AI Model Section ──
    public static string Settings_Ai_Title => Get(nameof(Settings_Ai_Title));
    public static string Settings_Ai_Desc => Get(nameof(Settings_Ai_Desc));
    public static string Settings_Ai_DefaultModelLabel => Get(nameof(Settings_Ai_DefaultModelLabel));
    public static string Settings_Ai_DefaultModelHint => Get(nameof(Settings_Ai_DefaultModelHint));
    public static string Settings_Ai_EffortLevelLabel => Get(nameof(Settings_Ai_EffortLevelLabel));
    public static string Settings_Ai_EffortLevelMax => Get(nameof(Settings_Ai_EffortLevelMax));
    public static string Settings_Ai_EffortLevelHint => Get(nameof(Settings_Ai_EffortLevelHint));
    public static string Settings_Ai_PermissionModeLabel => Get(nameof(Settings_Ai_PermissionModeLabel));
    public static string Settings_Ai_PermissionModeNormal => Get(nameof(Settings_Ai_PermissionModeNormal));
    public static string Settings_Ai_PermissionModePlan => Get(nameof(Settings_Ai_PermissionModePlan));
    public static string Settings_Ai_PermissionModeHint => Get(nameof(Settings_Ai_PermissionModeHint));
    public static string Settings_Ai_FallbackModelLabel => Get(nameof(Settings_Ai_FallbackModelLabel));
    public static string Settings_Ai_FallbackModelHint => Get(nameof(Settings_Ai_FallbackModelHint));
    public static string Settings_Ai_McpConfigPathLabel => Get(nameof(Settings_Ai_McpConfigPathLabel));
    public static string Settings_Ai_McpConfigPathHint => Get(nameof(Settings_Ai_McpConfigPathHint));
    public static string Settings_Ai_CodexDefaultModelLabel => Get(nameof(Settings_Ai_CodexDefaultModelLabel));
    public static string Settings_Ai_CodexDefaultModelHint => Get(nameof(Settings_Ai_CodexDefaultModelHint));
    public static string Settings_Ai_ReasoningEffortLabel => Get(nameof(Settings_Ai_ReasoningEffortLabel));
    public static string Settings_Ai_ReasoningEffortHint => Get(nameof(Settings_Ai_ReasoningEffortHint));
    public static string Settings_Ai_SandboxModeLabel => Get(nameof(Settings_Ai_SandboxModeLabel));
    public static string Settings_Ai_SandboxReadOnly => Get(nameof(Settings_Ai_SandboxReadOnly));
    public static string Settings_Ai_SandboxWorkspace => Get(nameof(Settings_Ai_SandboxWorkspace));
    public static string Settings_Ai_SandboxFullAccess => Get(nameof(Settings_Ai_SandboxFullAccess));
    public static string Settings_Ai_SandboxModeHint => Get(nameof(Settings_Ai_SandboxModeHint));
    public static string Settings_Ai_ApprovalPolicyLabel => Get(nameof(Settings_Ai_ApprovalPolicyLabel));
    public static string Settings_Ai_ApprovalAlways => Get(nameof(Settings_Ai_ApprovalAlways));
    public static string Settings_Ai_ApprovalOnRequest => Get(nameof(Settings_Ai_ApprovalOnRequest));
    public static string Settings_Ai_ApprovalAuto => Get(nameof(Settings_Ai_ApprovalAuto));
    public static string Settings_Ai_ApprovalPolicyHint => Get(nameof(Settings_Ai_ApprovalPolicyHint));
    public static string Settings_Ai_WebSearchLabel => Get(nameof(Settings_Ai_WebSearchLabel));
    public static string Settings_Ai_WebSearchHint => Get(nameof(Settings_Ai_WebSearchHint));
    public static string Settings_Ai_ReasoningMinimal => Get(nameof(Settings_Ai_ReasoningMinimal));
    public static string Settings_Ai_ReasoningLow => Get(nameof(Settings_Ai_ReasoningLow));
    public static string Settings_Ai_ReasoningMedium => Get(nameof(Settings_Ai_ReasoningMedium));
    public static string Settings_Ai_ReasoningHigh => Get(nameof(Settings_Ai_ReasoningHigh));
    public static string Settings_Ai_ReasoningXHigh => Get(nameof(Settings_Ai_ReasoningXHigh));

    // ── Settings: Notifications Section ──
    public static string Settings_Notifications_Title => Get(nameof(Settings_Notifications_Title));
    public static string Settings_Notifications_Desc => Get(nameof(Settings_Notifications_Desc));
    public static string Settings_Notifications_DesktopLabel => Get(nameof(Settings_Notifications_DesktopLabel));
    public static string Settings_Notifications_DesktopHint => Get(nameof(Settings_Notifications_DesktopHint));
    public static string Settings_Notifications_SoundLabel => Get(nameof(Settings_Notifications_SoundLabel));
    public static string Settings_Notifications_SoundHint => Get(nameof(Settings_Notifications_SoundHint));
    public static string Settings_Notifications_SoundTypeLabel => Get(nameof(Settings_Notifications_SoundTypeLabel));
    public static string Settings_Notifications_TestButton => Get(nameof(Settings_Notifications_TestButton));
    public static string Settings_Notifications_NativeActive => Get(nameof(Settings_Notifications_NativeActive));
    public static string Settings_Notifications_ScriptFallback => Get(nameof(Settings_Notifications_ScriptFallback));
    public static string Settings_Notifications_TestMsg => Get(nameof(Settings_Notifications_TestMsg));
    public static string Settings_Notifications_TestSent => Get(nameof(Settings_Notifications_TestSent));
    public static string Settings_Notifications_SoundDefault => Get(nameof(Settings_Notifications_SoundDefault));

    // ── Settings: Terminal Section ──
    public static string Settings_Terminal_Title => Get(nameof(Settings_Terminal_Title));
    public static string Settings_Terminal_Desc => Get(nameof(Settings_Terminal_Desc));
    public static string Settings_Terminal_ShellLabel => Get(nameof(Settings_Terminal_ShellLabel));
    public static string Settings_Terminal_ShellAuto(string detected) => Get(nameof(Settings_Terminal_ShellAuto), detected);
    public static string Settings_Terminal_ShellHint => Get(nameof(Settings_Terminal_ShellHint));

    // ── Settings: Git/Merge Section ──
    public static string Settings_GitMerge_Title => Get(nameof(Settings_GitMerge_Title));
    public static string Settings_GitMerge_Desc => Get(nameof(Settings_GitMerge_Desc));
    public static string Settings_GitMerge_DefaultStrategyLabel => Get(nameof(Settings_GitMerge_DefaultStrategyLabel));
    public static string Settings_GitMerge_DefaultStrategyHint => Get(nameof(Settings_GitMerge_DefaultStrategyHint));
    public static string Settings_GitMerge_AutoArchiveLabel => Get(nameof(Settings_GitMerge_AutoArchiveLabel));
    public static string Settings_GitMerge_AutoArchiveHint => Get(nameof(Settings_GitMerge_AutoArchiveHint));
    public static string Settings_GitMerge_AiPromptsTitle => Get(nameof(Settings_GitMerge_AiPromptsTitle));
    public static string Settings_GitMerge_AiPromptsDesc => Get(nameof(Settings_GitMerge_AiPromptsDesc));
    public static string Settings_GitMerge_CreatePrPromptLabel => Get(nameof(Settings_GitMerge_CreatePrPromptLabel));
    public static string Settings_GitMerge_PushPromptLabel => Get(nameof(Settings_GitMerge_PushPromptLabel));
    public static string Settings_GitMerge_ConflictPromptLabel => Get(nameof(Settings_GitMerge_ConflictPromptLabel));
    public static string Settings_GitMerge_RebasePromptLabel => Get(nameof(Settings_GitMerge_RebasePromptLabel));
    public static string Settings_GitMerge_ResetToDefault => Get(nameof(Settings_GitMerge_ResetToDefault));

    // ── Settings: Advanced Section ──
    public static string Settings_Advanced_Title => Get(nameof(Settings_Advanced_Title));
    public static string Settings_Advanced_Desc => Get(nameof(Settings_Advanced_Desc));
    public static string Settings_Advanced_DebugModeLabel => Get(nameof(Settings_Advanced_DebugModeLabel));
    public static string Settings_Advanced_DebugModeHint => Get(nameof(Settings_Advanced_DebugModeHint));
    public static string Settings_Advanced_EnvVarsTitle => Get(nameof(Settings_Advanced_EnvVarsTitle));
    public static string Settings_Advanced_EnvVarsHint => Get(nameof(Settings_Advanced_EnvVarsHint));
    public static string Settings_Advanced_EnvKeyPlaceholder => Get(nameof(Settings_Advanced_EnvKeyPlaceholder));
    public static string Settings_Advanced_EnvValuePlaceholder => Get(nameof(Settings_Advanced_EnvValuePlaceholder));

    // ── Settings: Updates Section ──
    public static string Settings_Updates_Title => Get(nameof(Settings_Updates_Title));
    public static string Settings_Updates_Desc => Get(nameof(Settings_Updates_Desc));
    public static string Settings_Updates_CurrentVersion => Get(nameof(Settings_Updates_CurrentVersion));
    public static string Settings_Updates_DevBuild => Get(nameof(Settings_Updates_DevBuild));
    public static string Settings_Updates_AutoCheckLabel => Get(nameof(Settings_Updates_AutoCheckLabel));
    public static string Settings_Updates_AutoCheckHint => Get(nameof(Settings_Updates_AutoCheckHint));
    public static string Settings_Updates_IntervalLabel => Get(nameof(Settings_Updates_IntervalLabel));
    public static string Settings_Updates_CheckButton => Get(nameof(Settings_Updates_CheckButton));
    public static string Settings_Updates_Checking => Get(nameof(Settings_Updates_Checking));
    public static string Settings_Updates_Ready => Get(nameof(Settings_Updates_Ready));
    public static string Settings_Updates_RestartButton => Get(nameof(Settings_Updates_RestartButton));
    public static string Settings_Updates_Downloading => Get(nameof(Settings_Updates_Downloading));
    public static string Settings_Updates_Available(string version) => Get(nameof(Settings_Updates_Available), version);
    public static string Settings_Updates_DownloadButton => Get(nameof(Settings_Updates_DownloadButton));
    public static string Settings_Updates_UpToDate => Get(nameof(Settings_Updates_UpToDate));
    public static string Settings_Updates_DevBuildWarning => Get(nameof(Settings_Updates_DevBuildWarning));
    public static string Settings_Updates_CheckFailed(string error) => Get(nameof(Settings_Updates_CheckFailed), error);
    public static string Settings_Updates_DownloadFailed(string error) => Get(nameof(Settings_Updates_DownloadFailed), error);
    public static string Settings_Updates_ReleaseNotesTitle => Get(nameof(Settings_Updates_ReleaseNotesTitle));
    public static string Settings_Updates_Interval_30 => Get(nameof(Settings_Updates_Interval_30));
    public static string Settings_Updates_Interval_60 => Get(nameof(Settings_Updates_Interval_60));
    public static string Settings_Updates_Interval_180 => Get(nameof(Settings_Updates_Interval_180));
    public static string Settings_Updates_Interval_360 => Get(nameof(Settings_Updates_Interval_360));
    public static string Settings_Updates_Interval_720 => Get(nameof(Settings_Updates_Interval_720));
    public static string Settings_Updates_Interval_1440 => Get(nameof(Settings_Updates_Interval_1440));

    // ── Settings: Integrations Section ──
    public static string Settings_Integrations_Title => Get(nameof(Settings_Integrations_Title));
    public static string Settings_Integrations_Desc => Get(nameof(Settings_Integrations_Desc));
    public static string Settings_Integrations_Checking => Get(nameof(Settings_Integrations_Checking));
    public static string Settings_Integrations_Detected => Get(nameof(Settings_Integrations_Detected));
    public static string Settings_Integrations_NotDetected => Get(nameof(Settings_Integrations_NotDetected));
    public static string Settings_Integrations_Recheck => Get(nameof(Settings_Integrations_Recheck));
    public static string Settings_Integrations_Version => Get(nameof(Settings_Integrations_Version));
    public static string Settings_Integrations_UpdateNeeded(string version) => Get(nameof(Settings_Integrations_UpdateNeeded), version);
    public static string Settings_Integrations_UpdateMethod => Get(nameof(Settings_Integrations_UpdateMethod));
    public static string Settings_Integrations_ClaudePathLabel => Get(nameof(Settings_Integrations_ClaudePathLabel));
    public static string Settings_Integrations_AutoDetect => Get(nameof(Settings_Integrations_AutoDetect));
    public static string Settings_Integrations_AutoDetectHint => Get(nameof(Settings_Integrations_AutoDetectHint));
    public static string Settings_Integrations_GitPathLabel => Get(nameof(Settings_Integrations_GitPathLabel));
    public static string Settings_Integrations_GhPathLabel => Get(nameof(Settings_Integrations_GhPathLabel));
    public static string Settings_Integrations_CodexPathLabel => Get(nameof(Settings_Integrations_CodexPathLabel));
    public static string Settings_Integrations_CodexOptional => Get(nameof(Settings_Integrations_CodexOptional));
    public static string Settings_Integrations_NotInstalled => Get(nameof(Settings_Integrations_NotInstalled));
    public static string Settings_Integrations_InstallMethod => Get(nameof(Settings_Integrations_InstallMethod));

    // ── Settings: Shortcuts Section ──
    public static string Settings_Shortcuts_Switch => Get(nameof(Settings_Shortcuts_Switch));
    public static string Settings_Shortcuts_DeleteSession => Get(nameof(Settings_Shortcuts_DeleteSession));
    public static string Settings_Shortcuts_ExpandAll => Get(nameof(Settings_Shortcuts_ExpandAll));
    public static string Settings_Shortcuts_CollapseAll => Get(nameof(Settings_Shortcuts_CollapseAll));
    public static string Settings_Shortcuts_ToggleSidebar => Get(nameof(Settings_Shortcuts_ToggleSidebar));
    public static string Settings_Shortcuts_FocusInput => Get(nameof(Settings_Shortcuts_FocusInput));
    public static string Settings_Shortcuts_ToggleTheme => Get(nameof(Settings_Shortcuts_ToggleTheme));
    public static string Settings_Shortcuts_TogglePlanMode => Get(nameof(Settings_Shortcuts_TogglePlanMode));
    public static string Settings_Shortcuts_ApprovePlan => Get(nameof(Settings_Shortcuts_ApprovePlan));
    public static string Settings_Shortcuts_ToggleEffort => Get(nameof(Settings_Shortcuts_ToggleEffort));
    public static string Settings_Shortcuts_ToggleSync => Get(nameof(Settings_Shortcuts_ToggleSync));
    public static string Settings_Shortcuts_SendMessage => Get(nameof(Settings_Shortcuts_SendMessage));
    public static string Settings_Shortcuts_Newline => Get(nameof(Settings_Shortcuts_Newline));
    public static string Settings_Shortcuts_CloseOverlay => Get(nameof(Settings_Shortcuts_CloseOverlay));
    public static string Settings_Shortcuts_TerminalCopy => Get(nameof(Settings_Shortcuts_TerminalCopy));
    public static string Settings_Shortcuts_TerminalPaste => Get(nameof(Settings_Shortcuts_TerminalPaste));

    // ── Settings: Claude CLI Settings Section ──
    public static string Settings_Claude_Title => Get(nameof(Settings_Claude_Title));
    public static string Settings_Claude_ScopeGlobal => Get(nameof(Settings_Claude_ScopeGlobal));
    public static string Settings_Claude_ScopeProject => Get(nameof(Settings_Claude_ScopeProject));
    public static string Settings_Claude_ScopeLocal => Get(nameof(Settings_Claude_ScopeLocal));
    public static string Settings_Claude_ModelBehaviorTitle => Get(nameof(Settings_Claude_ModelBehaviorTitle));
    public static string Settings_Claude_ModelLabel => Get(nameof(Settings_Claude_ModelLabel));
    public static string Settings_Claude_ModelHint => Get(nameof(Settings_Claude_ModelHint));
    public static string Settings_Claude_EffortLabel => Get(nameof(Settings_Claude_EffortLabel));
    public static string Settings_Claude_EffortHint => Get(nameof(Settings_Claude_EffortHint));
    public static string Settings_Claude_DefaultModeLabel => Get(nameof(Settings_Claude_DefaultModeLabel));
    public static string Settings_Claude_DefaultModeHint => Get(nameof(Settings_Claude_DefaultModeHint));
    public static string Settings_Claude_NotSet => Get(nameof(Settings_Claude_NotSet));
    public static string Settings_Claude_AlwaysThinkingLabel => Get(nameof(Settings_Claude_AlwaysThinkingLabel));
    public static string Settings_Claude_AlwaysThinkingHint => Get(nameof(Settings_Claude_AlwaysThinkingHint));
    public static string Settings_Claude_AutoMemoryLabel => Get(nameof(Settings_Claude_AutoMemoryLabel));
    public static string Settings_Claude_AutoMemoryHint => Get(nameof(Settings_Claude_AutoMemoryHint));
    public static string Settings_Claude_PermissionsTitle => Get(nameof(Settings_Claude_PermissionsTitle));
    public static string Settings_Claude_EnvVarsTitle => Get(nameof(Settings_Claude_EnvVarsTitle));
    public static string Settings_Claude_EnvVarsHint => Get(nameof(Settings_Claude_EnvVarsHint));
    public static string Settings_Claude_AddDirsTitle => Get(nameof(Settings_Claude_AddDirsTitle));
    public static string Settings_Claude_AddDirsHint => Get(nameof(Settings_Claude_AddDirsHint));
    public static string Settings_Claude_McpDesc => Get(nameof(Settings_Claude_McpDesc));
    public static string Settings_Claude_Manage => Get(nameof(Settings_Claude_Manage));
    public static string Settings_Claude_NoHooks => Get(nameof(Settings_Claude_NoHooks));
    public static string Settings_Claude_Saved => Get(nameof(Settings_Claude_Saved));
    public static string Settings_Claude_SaveFailed(string error) => Get(nameof(Settings_Claude_SaveFailed), error);

    // ── Settings: Workspace Settings ──
    public static string Settings_Workspace_TabGeneral => Get(nameof(Settings_Workspace_TabGeneral));
    public static string Settings_Workspace_TabPrompts => Get(nameof(Settings_Workspace_TabPrompts));
    public static string Settings_Workspace_GeneralDesc => Get(nameof(Settings_Workspace_GeneralDesc));
    public static string Settings_Workspace_NameLabel => Get(nameof(Settings_Workspace_NameLabel));
    public static string Settings_Workspace_UseAppDefault => Get(nameof(Settings_Workspace_UseAppDefault));
    public static string Settings_Workspace_DefaultModelHint => Get(nameof(Settings_Workspace_DefaultModelHint));
    public static string Settings_Workspace_RepoPathLabel => Get(nameof(Settings_Workspace_RepoPathLabel));
    public static string Settings_Workspace_RepoUrlLabel => Get(nameof(Settings_Workspace_RepoUrlLabel));
    public static string Settings_Workspace_SystemPromptLabel => Get(nameof(Settings_Workspace_SystemPromptLabel));
    public static string Settings_Workspace_SystemPromptPlaceholder => Get(nameof(Settings_Workspace_SystemPromptPlaceholder));
    public static string Settings_Workspace_SystemPromptHint => Get(nameof(Settings_Workspace_SystemPromptHint));
    public static string Settings_Workspace_GitDesc => Get(nameof(Settings_Workspace_GitDesc));
    public static string Settings_Workspace_DefaultBranchLabel => Get(nameof(Settings_Workspace_DefaultBranchLabel));
    public static string Settings_Workspace_DefaultBranchHint => Get(nameof(Settings_Workspace_DefaultBranchHint));
    public static string Settings_Workspace_DefaultBranchAutoHint => Get(nameof(Settings_Workspace_DefaultBranchAutoHint));
    public static string Settings_Workspace_DefaultRemoteLabel => Get(nameof(Settings_Workspace_DefaultRemoteLabel));
    public static string Settings_Workspace_PromptsDesc => Get(nameof(Settings_Workspace_PromptsDesc));
    public static string Settings_Workspace_CodeReviewLabel => Get(nameof(Settings_Workspace_CodeReviewLabel));
    public static string Settings_Workspace_CodeReviewPlaceholder => Get(nameof(Settings_Workspace_CodeReviewPlaceholder));
    public static string Settings_Workspace_GeneralPromptLabel => Get(nameof(Settings_Workspace_GeneralPromptLabel));
    public static string Settings_Workspace_GeneralPromptPlaceholder => Get(nameof(Settings_Workspace_GeneralPromptPlaceholder));
    public static string Settings_Workspace_DangerZoneTitle => Get(nameof(Settings_Workspace_DangerZoneTitle));
    public static string Settings_Workspace_DeleteButton => Get(nameof(Settings_Workspace_DeleteButton));
    public static string Settings_Workspace_DeleteWarning => Get(nameof(Settings_Workspace_DeleteWarning));
    public static string Settings_Workspace_DeleteDialogTitle => Get(nameof(Settings_Workspace_DeleteDialogTitle));
    public static string Settings_Workspace_DeleteDialogMsg(string name) => Get(nameof(Settings_Workspace_DeleteDialogMsg), name);
    public static string Settings_Workspace_DeleteSuccess(string name) => Get(nameof(Settings_Workspace_DeleteSuccess), name);
    public static string Settings_Workspace_DeleteError(string error) => Get(nameof(Settings_Workspace_DeleteError), error);

    // ── Chat Components ──
    public static string Chat_Placeholder_NoSession => Get(nameof(Chat_Placeholder_NoSession));
    public static string Chat_Placeholder_Streaming => Get(nameof(Chat_Placeholder_Streaming));
    public static string Chat_Placeholder_PlanFeedback => Get(nameof(Chat_Placeholder_PlanFeedback));
    public static string Chat_Placeholder_PlanMode => Get(nameof(Chat_Placeholder_PlanMode));
    public static string Chat_Placeholder_AcceptEditsMode => Get(nameof(Chat_Placeholder_AcceptEditsMode));
    public static string Chat_Placeholder_Default => Get(nameof(Chat_Placeholder_Default));
    public static string Chat_PastedAsFile => Get(nameof(Chat_PastedAsFile));
    public static string Chat_LandingSubtitle => Get(nameof(Chat_LandingSubtitle));
    public static string Chat_WelcomeTitle => Get(nameof(Chat_WelcomeTitle));
    public static string Chat_Streaming_Preparing => Get(nameof(Chat_Streaming_Preparing));
    public static string Chat_Streaming_Sending => Get(nameof(Chat_Streaming_Sending));
    public static string Chat_Thinking => Get(nameof(Chat_Thinking));
    public static string Chat_Toolbar_AttachFile => Get(nameof(Chat_Toolbar_AttachFile));
    public static string Chat_Toolbar_WebSearchOn => Get(nameof(Chat_Toolbar_WebSearchOn));
    public static string Chat_Toolbar_WebSearchOff => Get(nameof(Chat_Toolbar_WebSearchOff));
    public static string Chat_Toolbar_ViewPlan => Get(nameof(Chat_Toolbar_ViewPlan));
    public static string Chat_Toolbar_Stop => Get(nameof(Chat_Toolbar_Stop));
    public static string Chat_Toolbar_ToolPicker => Get(nameof(Chat_Toolbar_ToolPicker));
    public static string Chat_ToolPicker_McpServers => Get(nameof(Chat_ToolPicker_McpServers));
    public static string Chat_ToolPicker_ManageMcp => Get(nameof(Chat_ToolPicker_ManageMcp));
    public static string Chat_ToolPicker_AllEnabled => Get(nameof(Chat_ToolPicker_AllEnabled));
    public static string Chat_ToolPicker_DisabledCount(int count) => Get(nameof(Chat_ToolPicker_DisabledCount), count.ToString());
    public static string Chat_PermissionMode_PlanTooltip => Get(nameof(Chat_PermissionMode_PlanTooltip));
    public static string Chat_PermissionMode_NormalTooltip => Get(nameof(Chat_PermissionMode_NormalTooltip));
    public static string Chat_EffortLevel_AutoTooltip(string maxLevel) => Get(nameof(Chat_EffortLevel_AutoTooltip), maxLevel);
    public static string Chat_TokenTooltip_Total(string total, string window, string pct) => Get(nameof(Chat_TokenTooltip_Total), total, window, pct);
    public static string Chat_TokenTooltip_Input(string value) => Get(nameof(Chat_TokenTooltip_Input), value);
    public static string Chat_TokenTooltip_Output(string value) => Get(nameof(Chat_TokenTooltip_Output), value);
    public static string Chat_TokenTooltip_Cost(string value) => Get(nameof(Chat_TokenTooltip_Cost), value);
    public static string Chat_Plan_ReviewTitle => Get(nameof(Chat_Plan_ReviewTitle));
    public static string Chat_Plan_LoadingContent => Get(nameof(Chat_Plan_LoadingContent));
    public static string Chat_Plan_Approve => Get(nameof(Chat_Plan_Approve));
    public static string Chat_Plan_Reject => Get(nameof(Chat_Plan_Reject));
    public static string Chat_Notification_PlanReady => Get(nameof(Chat_Notification_PlanReady));
    public static string Chat_Notification_NeedsResponse => Get(nameof(Chat_Notification_NeedsResponse));
    public static string Chat_Notification_Done => Get(nameof(Chat_Notification_Done));
    public static string Chat_Chaining(string skillName) => Get(nameof(Chat_Chaining), skillName);
    public static string Chat_UserRoleName => Get(nameof(Chat_UserRoleName));

    // ── Sidebar Components ──
    public static string Sidebar_WorkspaceSettings => Get(nameof(Sidebar_WorkspaceSettings));
    public static string Sidebar_DeleteWorkspace => Get(nameof(Sidebar_DeleteWorkspace));
    public static string Sidebar_WelcomeMessage => Get(nameof(Sidebar_WelcomeMessage));
    public static string Sidebar_NewWorkspace => Get(nameof(Sidebar_NewWorkspace));
    public static string Sidebar_OpenFolder => Get(nameof(Sidebar_OpenFolder));
    public static string Sidebar_OpenInIde(string ideName) => Get(nameof(Sidebar_OpenInIde), ideName);
    public static string Sidebar_Search => Get(nameof(Sidebar_Search));
    public static string Sidebar_Status_Writing => Get(nameof(Sidebar_Status_Writing));
    public static string Sidebar_Status_Planning => Get(nameof(Sidebar_Status_Planning));
    public static string Sidebar_Status_Working => Get(nameof(Sidebar_Status_Working));
    public static string Sidebar_Tool_Bash => Get(nameof(Sidebar_Tool_Bash));
    public static string Sidebar_Tool_Read => Get(nameof(Sidebar_Tool_Read));
    public static string Sidebar_Tool_Write => Get(nameof(Sidebar_Tool_Write));
    public static string Sidebar_Tool_Edit => Get(nameof(Sidebar_Tool_Edit));
    public static string Sidebar_Tool_Glob => Get(nameof(Sidebar_Tool_Glob));
    public static string Sidebar_Tool_Grep => Get(nameof(Sidebar_Tool_Grep));
    public static string Sidebar_Tool_Agent => Get(nameof(Sidebar_Tool_Agent));
    public static string Sidebar_Tool_Default => Get(nameof(Sidebar_Tool_Default));
    public static string Sidebar_CreateWorkspace_Title => Get(nameof(Sidebar_CreateWorkspace_Title));
    public static string Sidebar_CreateWorkspace_Subtitle => Get(nameof(Sidebar_CreateWorkspace_Subtitle));
    public static string Sidebar_CreateWorkspace_CloneFromUrl => Get(nameof(Sidebar_CreateWorkspace_CloneFromUrl));
    public static string Sidebar_CreateWorkspace_LocalRepository => Get(nameof(Sidebar_CreateWorkspace_LocalRepository));
    public static string Sidebar_CreateWorkspace_RepoUrlLabel => Get(nameof(Sidebar_CreateWorkspace_RepoUrlLabel));
    public static string Sidebar_CreateWorkspace_ClonePathLabel => Get(nameof(Sidebar_CreateWorkspace_ClonePathLabel));
    public static string Sidebar_CreateWorkspace_Browse => Get(nameof(Sidebar_CreateWorkspace_Browse));
    public static string Sidebar_CreateWorkspace_WorkspaceNameLabel => Get(nameof(Sidebar_CreateWorkspace_WorkspaceNameLabel));
    public static string Sidebar_CreateWorkspace_DefaultModelLabel => Get(nameof(Sidebar_CreateWorkspace_DefaultModelLabel));
    public static string Sidebar_CreateWorkspace_DefaultModelHelper => Get(nameof(Sidebar_CreateWorkspace_DefaultModelHelper));
    public static string Sidebar_CreateWorkspace_UseAppDefault => Get(nameof(Sidebar_CreateWorkspace_UseAppDefault));
    public static string Sidebar_CreateWorkspace_RepoPathLabel => Get(nameof(Sidebar_CreateWorkspace_RepoPathLabel));
    public static string Sidebar_CreateWorkspace_InitGit => Get(nameof(Sidebar_CreateWorkspace_InitGit));
    public static string Sidebar_CreateWorkspace_Create => Get(nameof(Sidebar_CreateWorkspace_Create));
    public static string Sidebar_CreateWorkspace_Cloning => Get(nameof(Sidebar_CreateWorkspace_Cloning));
    public static string Sidebar_CreateWorkspace_CreationFailed => Get(nameof(Sidebar_CreateWorkspace_CreationFailed));
    public static string Sidebar_CreateWorkspace_OperationCancelled => Get(nameof(Sidebar_CreateWorkspace_OperationCancelled));
    public static string Sidebar_CreateWorkspace_SettingUp => Get(nameof(Sidebar_CreateWorkspace_SettingUp));
    public static string Sidebar_CreateWorkspace_NotGitRepo => Get(nameof(Sidebar_CreateWorkspace_NotGitRepo));
    public static string Sidebar_CreateWorkspace_ValidGitRepo => Get(nameof(Sidebar_CreateWorkspace_ValidGitRepo));
    public static string Sidebar_CreateWorkspace_GitInitFailed(string error) => Get(nameof(Sidebar_CreateWorkspace_GitInitFailed), error);
    public static string Sidebar_CreateWorkspace_GitInitialized => Get(nameof(Sidebar_CreateWorkspace_GitInitialized));
    public static string Sidebar_CreateWorkspace_GitInitError => Get(nameof(Sidebar_CreateWorkspace_GitInitError));
    public static string Sidebar_AddSession_HeaderNew => Get(nameof(Sidebar_AddSession_HeaderNew));
    public static string Sidebar_AddSession_NewWorktreeTitle => Get(nameof(Sidebar_AddSession_NewWorktreeTitle));
    public static string Sidebar_AddSession_NewWorktreeDesc => Get(nameof(Sidebar_AddSession_NewWorktreeDesc));
    public static string Sidebar_AddSession_LocalDirectoryTitle => Get(nameof(Sidebar_AddSession_LocalDirectoryTitle));
    public static string Sidebar_AddSession_LocalDirectoryDesc => Get(nameof(Sidebar_AddSession_LocalDirectoryDesc));
    public static string Sidebar_AddSession_CodexWorktreeTitle => Get(nameof(Sidebar_AddSession_CodexWorktreeTitle));
    public static string Sidebar_AddSession_CodexWorktreeDesc => Get(nameof(Sidebar_AddSession_CodexWorktreeDesc));
    public static string Sidebar_AddSession_CodexLocalTitle => Get(nameof(Sidebar_AddSession_CodexLocalTitle));
    public static string Sidebar_AddSession_CodexLocalDesc => Get(nameof(Sidebar_AddSession_CodexLocalDesc));
    public static string Sidebar_Changes_ConflictTooltip(string filePath) => Get(nameof(Sidebar_Changes_ConflictTooltip), filePath);
    public static string Sidebar_Changes_GroupModified => Get(nameof(Sidebar_Changes_GroupModified));
    public static string Sidebar_Changes_GroupAdded => Get(nameof(Sidebar_Changes_GroupAdded));
    public static string Sidebar_Changes_GroupDeleted => Get(nameof(Sidebar_Changes_GroupDeleted));
    public static string Sidebar_Changes_GroupRenamed => Get(nameof(Sidebar_Changes_GroupRenamed));
    public static string Sidebar_Changes_GroupUntracked => Get(nameof(Sidebar_Changes_GroupUntracked));
    public static string Sidebar_Changes_GroupOther => Get(nameof(Sidebar_Changes_GroupOther));
    public static string Sidebar_Explorer_NoFilesFound => Get(nameof(Sidebar_Explorer_NoFilesFound));
    public static string Sidebar_Explorer_FileReadError(string error) => Get(nameof(Sidebar_Explorer_FileReadError), error);

    public static string Snackbar_AppUpdateAction => Get(nameof(Snackbar_AppUpdateAction));
    public static string Snackbar_AppUpdateReady => Get(nameof(Snackbar_AppUpdateReady));
    public static string Snackbar_AppUpdateRestartAction => Get(nameof(Snackbar_AppUpdateRestartAction));
    public static string Snackbar_SessionDeleted => Get(nameof(Snackbar_SessionDeleted));
    public static string Snackbar_SettingsSaved => Get(nameof(Snackbar_SettingsSaved));
    public static string Tool_AgentSingle => Get(nameof(Tool_AgentSingle));
    public static string Tool_BashSingle => Get(nameof(Tool_BashSingle));
    public static string Tool_FileEditedSingle => Get(nameof(Tool_FileEditedSingle));
    public static string Tool_FileWrittenSingle => Get(nameof(Tool_FileWrittenSingle));
    public static string Tool_GlobDone => Get(nameof(Tool_GlobDone));
    public static string Tool_GrepSingle => Get(nameof(Tool_GrepSingle));
    public static string Tool_NotebookSingle => Get(nameof(Tool_NotebookSingle));
    public static string Tool_TodoWriteDone => Get(nameof(Tool_TodoWriteDone));
    public static string Tool_WebFetchSingle => Get(nameof(Tool_WebFetchSingle));
    public static string Tool_WebSearchSingle => Get(nameof(Tool_WebSearchSingle));

    public static string Snackbar_AppUpdateAvailable(string version)
    {
        return Get(nameof(Snackbar_AppUpdateAvailable), version);
    }

    public static string Snackbar_ClaudeUpdateRequired(string current, string required)
    {
        return Get(nameof(Snackbar_ClaudeUpdateRequired), current, required);
    }

    public static string Snackbar_ConflictDetected(string branchName)
    {
        return Get(nameof(Snackbar_ConflictDetected), branchName);
    }

    public static string Snackbar_IssueCreated(int number)
    {
        return Get(nameof(Snackbar_IssueCreated), number);
    }

    public static string Snackbar_IssueLinked(int number)
    {
        return Get(nameof(Snackbar_IssueLinked), number);
    }

    public static string Snackbar_MergeError(string error)
    {
        return Get(nameof(Snackbar_MergeError), error);
    }

    public static string Snackbar_MergeSuccess(string branchName)
    {
        return Get(nameof(Snackbar_MergeSuccess), branchName);
    }

    public static string Snackbar_PrCreated(int prNumber)
    {
        return Get(nameof(Snackbar_PrCreated), prNumber);
    }

    public static string Snackbar_PrCreateError(string error)
    {
        return Get(nameof(Snackbar_PrCreateError), error);
    }

    public static string Snackbar_PushError(string error)
    {
        return Get(nameof(Snackbar_PushError), error);
    }

    // ── Snackbar ──

    public static string Snackbar_PushSuccess(string branchName)
    {
        return Get(nameof(Snackbar_PushSuccess), branchName);
    }

    public static string Snackbar_StreamingError(string error)
    {
        return Get(nameof(Snackbar_StreamingError), error);
    }

    public static string Snackbar_WorkspaceCreated(string name)
    {
        return Get(nameof(Snackbar_WorkspaceCreated), name);
    }

    public static string Snackbar_WorkspaceDeleted(string name)
    {
        return Get(nameof(Snackbar_WorkspaceDeleted), name);
    }

    public static string Tool_AgentMultiple(int count)
    {
        return Get(nameof(Tool_AgentMultiple), count);
    }

    public static string Tool_BashMultiple(int count)
    {
        return Get(nameof(Tool_BashMultiple), count);
    }

    public static string Tool_DefaultMultiple(string name, int count)
    {
        return Get(nameof(Tool_DefaultMultiple), name, count);
    }

    public static string Tool_FileEditedMultiple(int count)
    {
        return Get(nameof(Tool_FileEditedMultiple), count);
    }

    // ── Tool Display ──

    public static string Tool_FilesRead(int count)
    {
        return Get(nameof(Tool_FilesRead), count);
    }

    public static string Tool_FileWrittenMultiple(int count)
    {
        return Get(nameof(Tool_FileWrittenMultiple), count);
    }

    public static string Tool_GlobHint(int lineCount)
    {
        return Get(nameof(Tool_GlobHint), lineCount);
    }

    public static string Tool_GrepHint(int lineCount)
    {
        return Get(nameof(Tool_GrepHint), lineCount);
    }

    public static string Tool_GrepMultiple(int count)
    {
        return Get(nameof(Tool_GrepMultiple), count);
    }

    public static string Tool_NotebookMultiple(int count)
    {
        return Get(nameof(Tool_NotebookMultiple), count);
    }

    public static string Tool_ReadHint(int lineCount)
    {
        return Get(nameof(Tool_ReadHint), lineCount);
    }

    public static string Tool_WebFetchMultiple(int count)
    {
        return Get(nameof(Tool_WebFetchMultiple), count);
    }

    public static string Tool_WebSearchMultiple(int count)
    {
        return Get(nameof(Tool_WebSearchMultiple), count);
    }

    // ── Layout Components ──
    public static string Layout_SidebarToolbar_NotificationHistory => Get(nameof(Layout_SidebarToolbar_NotificationHistory));
    public static string Layout_SidebarToolbar_CollapseSidebar => Get(nameof(Layout_SidebarToolbar_CollapseSidebar));
    public static string Layout_SidebarToolbar_ExpandSidebar => Get(nameof(Layout_SidebarToolbar_ExpandSidebar));
    public static string Layout_MainTabBar_CloseTab => Get(nameof(Layout_MainTabBar_CloseTab));
    public static string Layout_MainTabBar_CloseOtherTabs => Get(nameof(Layout_MainTabBar_CloseOtherTabs));
    public static string Layout_MainTabBar_CloseTabsToRight => Get(nameof(Layout_MainTabBar_CloseTabsToRight));
    public static string Layout_MainTabBar_CloseAllTabs => Get(nameof(Layout_MainTabBar_CloseAllTabs));
    public static string Layout_MainToolbar_NewConversation => Get(nameof(Layout_MainToolbar_NewConversation));
    public static string Layout_MainToolbar_Branch(string branch) => Get(nameof(Layout_MainToolbar_Branch), branch);
    public static string Layout_MainToolbar_PlanModeTooltip => Get(nameof(Layout_MainToolbar_PlanModeTooltip));
    public static string Layout_MainToolbar_EffortLevelTooltip(string level) => Get(nameof(Layout_MainToolbar_EffortLevelTooltip), level);
    public static string Layout_MainToolbar_SelectConversation => Get(nameof(Layout_MainToolbar_SelectConversation));
    public static string Layout_MainToolbar_OpenFolder => Get(nameof(Layout_MainToolbar_OpenFolder));
    public static string Layout_MainToolbar_OpenInIde(string ideName) => Get(nameof(Layout_MainToolbar_OpenInIde), ideName);
    public static string Layout_MainToolbar_SyncDisable(string mod) => Get(nameof(Layout_MainToolbar_SyncDisable), mod);
    public static string Layout_MainToolbar_SyncBlockedByOther => Get(nameof(Layout_MainToolbar_SyncBlockedByOther));
    public static string Layout_MainToolbar_SyncEnable(string mod) => Get(nameof(Layout_MainToolbar_SyncEnable), mod);
    public static string Layout_MainToolbar_SyncDisabled_Snackbar => Get(nameof(Layout_MainToolbar_SyncDisabled_Snackbar));
    public static string Layout_MainToolbar_SyncSuccess => Get(nameof(Layout_MainToolbar_SyncSuccess));
    public static string Layout_MainToolbar_SyncFailed => Get(nameof(Layout_MainToolbar_SyncFailed));
    public static string Layout_MainToolbar_SyncError(string message) => Get(nameof(Layout_MainToolbar_SyncError), message);
    public static string Layout_MainToolbar_EffortLow => Get(nameof(Layout_MainToolbar_EffortLow));
    public static string Layout_MainToolbar_EffortHigh => Get(nameof(Layout_MainToolbar_EffortHigh));
    public static string Layout_MainToolbar_EffortXHigh => Get(nameof(Layout_MainToolbar_EffortXHigh));
    public static string Layout_AppClose_Title => Get(nameof(Layout_AppClose_Title));
    public static string Layout_AppClose_Message => Get(nameof(Layout_AppClose_Message));
    public static string Layout_AppClose_Confirm => Get(nameof(Layout_AppClose_Confirm));
    public static string Layout_Update_Available => Get(nameof(Layout_Update_Available));
    public static string Layout_Update_AvailableDesc(string version) => Get(nameof(Layout_Update_AvailableDesc), version);
    public static string Layout_Update_NotificationTitle => Get(nameof(Layout_Update_NotificationTitle));
    public static string Layout_Update_NotificationBody(string version) => Get(nameof(Layout_Update_NotificationBody), version);
    public static string Layout_Migration_Title => Get(nameof(Layout_Migration_Title));
    public static string Layout_Migration_Message(int count) => Get(nameof(Layout_Migration_Message), count);
    public static string Layout_Migration_Import => Get(nameof(Layout_Migration_Import));
    public static string Layout_Migration_Later => Get(nameof(Layout_Migration_Later));
    public static string Layout_Migration_Skip => Get(nameof(Layout_Migration_Skip));
    public static string Layout_Migration_DoneTitle => Get(nameof(Layout_Migration_DoneTitle));
    public static string Layout_Migration_DoneRestart(int count) => Get(nameof(Layout_Migration_DoneRestart), count);
    public static string Layout_Migration_Summary(int copied, int skipped) => Get(nameof(Layout_Migration_Summary), copied, skipped);
    public static string Layout_Migration_SummaryFailed(int failed) => Get(nameof(Layout_Migration_SummaryFailed), failed);
    public static string Layout_Migration_FolderDeleteFailed => Get(nameof(Layout_Migration_FolderDeleteFailed));
    public static string Layout_Migration_DeleteFolderFailed => Get(nameof(Layout_Migration_DeleteFolderFailed));

    // ── Gamification & Dashboard ──
    public static string GetLevelName(int level) => Get($"Level_{level}");
    public static string GetAchievementName(string id) => Get("Achievement_" + id.Replace('-', '_') + "_Name");
    public static string GetAchievementDesc(string id) => Get("Achievement_" + id.Replace('-', '_') + "_Desc");
    public static string Gamification_Config_Model => Get(nameof(Gamification_Config_Model));
    public static string Gamification_Config_EffortLevel => Get(nameof(Gamification_Config_EffortLevel));
    public static string Gamification_Config_DefaultMode => Get(nameof(Gamification_Config_DefaultMode));
    public static string Gamification_Config_Permissions => Get(nameof(Gamification_Config_Permissions));
    public static string Gamification_Config_Hooks => Get(nameof(Gamification_Config_Hooks));
    public static string Gamification_Config_McpServers => Get(nameof(Gamification_Config_McpServers));
    public static string Dashboard_Title => Get(nameof(Dashboard_Title));
    public static string Dashboard_RefreshTooltip => Get(nameof(Dashboard_RefreshTooltip));
    public static string Dashboard_NextLevel(string levelName) => Get(nameof(Dashboard_NextLevel), levelName);
    public static string Dashboard_Stat_Projects(int n) => Get(nameof(Dashboard_Stat_Projects), n);
    public static string Dashboard_Stat_Achievements(int unlocked, int total) => Get(nameof(Dashboard_Stat_Achievements), unlocked, total);
    public static string Dashboard_Stat_StreakDays(int n) => Get(nameof(Dashboard_Stat_StreakDays), n);
    public static string Dashboard_ConfigCompleteness => Get(nameof(Dashboard_ConfigCompleteness));
    public static string Dashboard_Achievement_Title => Get(nameof(Dashboard_Achievement_Title));
    public static string Dashboard_Achievement_All => Get(nameof(Dashboard_Achievement_All));
    public static string Dashboard_Achievement_Rarity_Common => Get(nameof(Dashboard_Achievement_Rarity_Common));
    public static string Dashboard_Achievement_Rarity_Rare => Get(nameof(Dashboard_Achievement_Rarity_Rare));
    public static string Dashboard_Achievement_Rarity_Epic => Get(nameof(Dashboard_Achievement_Rarity_Epic));
    public static string Dashboard_Achievement_Rarity_Legendary => Get(nameof(Dashboard_Achievement_Rarity_Legendary));
    public static string Dashboard_Achievement_Category_Config => Get(nameof(Dashboard_Achievement_Category_Config));
    public static string Dashboard_Achievement_Category_Usage => Get(nameof(Dashboard_Achievement_Category_Usage));
    public static string Dashboard_Achievement_Category_Streak => Get(nameof(Dashboard_Achievement_Category_Streak));
    public static string Dashboard_Achievement_Category_Mastery => Get(nameof(Dashboard_Achievement_Category_Mastery));
    public static string Dashboard_Achievement_Category_Explorer => Get(nameof(Dashboard_Achievement_Category_Explorer));
    public static string Dashboard_Achievement_Category_Efficiency => Get(nameof(Dashboard_Achievement_Category_Efficiency));
    public static string Dashboard_Achievement_Category_Time => Get(nameof(Dashboard_Achievement_Category_Time));
    public static string Dashboard_Achievement_Category_Economy => Get(nameof(Dashboard_Achievement_Category_Economy));
    public static string Dashboard_Achievement_Category_Pattern => Get(nameof(Dashboard_Achievement_Category_Pattern));
    public static string Dashboard_Heatmap_Title => Get(nameof(Dashboard_Heatmap_Title));
    public static string Dashboard_Heatmap_Tooltip(int messages, int sessions, int tools) => Get(nameof(Dashboard_Heatmap_Tooltip), messages, sessions, tools);
    public static string Dashboard_Heatmap_Less => Get(nameof(Dashboard_Heatmap_Less));
    public static string Dashboard_Heatmap_More => Get(nameof(Dashboard_Heatmap_More));
    public static string Dashboard_Heatmap_DaysTracked(int days) => Get(nameof(Dashboard_Heatmap_DaysTracked), days);
    public static string Dashboard_Heatmap_DateFormat => Get(nameof(Dashboard_Heatmap_DateFormat));
    public static string Dashboard_Cost_Title => Get(nameof(Dashboard_Cost_Title));
    public static string Dashboard_Cost_Today => Get(nameof(Dashboard_Cost_Today));
    public static string Dashboard_Cost_ThisMonth => Get(nameof(Dashboard_Cost_ThisMonth));
    public static string Dashboard_Cost_Projection => Get(nameof(Dashboard_Cost_Projection));
    public static string Dashboard_Cost_PerMonth => Get(nameof(Dashboard_Cost_PerMonth));
    public static string Dashboard_Cost_ViewAnalytics => Get(nameof(Dashboard_Cost_ViewAnalytics));
    public static string Dashboard_Session_Title => Get(nameof(Dashboard_Session_Title));
    public static string Dashboard_Session_CostTooltip => Get(nameof(Dashboard_Session_CostTooltip));
    public static string Dashboard_Session_ViewAll => Get(nameof(Dashboard_Session_ViewAll));
    public static string Dashboard_Stats_Sessions => Get(nameof(Dashboard_Stats_Sessions));
    public static string Dashboard_Stats_Messages => Get(nameof(Dashboard_Stats_Messages));
    public static string Dashboard_Stats_ToolCalls => Get(nameof(Dashboard_Stats_ToolCalls));
    public static string Dashboard_Stats_ActiveDays => Get(nameof(Dashboard_Stats_ActiveDays));
    public static string Dashboard_Streak_Title => Get(nameof(Dashboard_Streak_Title));
    public static string Dashboard_Streak_Unit => Get(nameof(Dashboard_Streak_Unit));
    public static string Dashboard_Streak_Longest(int days) => Get(nameof(Dashboard_Streak_Longest), days);
    public static string Dashboard_Streak_LastActive => Get(nameof(Dashboard_Streak_LastActive));

    // ── Phase 8: MCP, Hooks, Rules, Instructions, Memory, Plugins, Accounts, Sessions, Notifications, Onboarding, WhatsNew, Setup, TemplateGallery, ToolCallCard ──

    public static string Accounts_AddHint => Get(nameof(Accounts_AddHint));
    public static string Accounts_AddNew => Get(nameof(Accounts_AddNew));
    public static string Accounts_Card_Active => Get(nameof(Accounts_Card_Active));
    public static string Accounts_Card_AllModels => Get(nameof(Accounts_Card_AllModels));
    public static string Accounts_Card_CurrentSession => Get(nameof(Accounts_Card_CurrentSession));
    public static string Accounts_Card_Delete => Get(nameof(Accounts_Card_Delete));
    public static string Accounts_Card_Deleted(object arg0) => Get(nameof(Accounts_Card_Deleted), arg0);
    public static string Accounts_Card_EditName => Get(nameof(Accounts_Card_EditName));
    public static string Accounts_Card_NoBackup => Get(nameof(Accounts_Card_NoBackup));
    public static string Accounts_Card_RefreshToken => Get(nameof(Accounts_Card_RefreshToken));
    public static string Accounts_Card_RefreshUsage => Get(nameof(Accounts_Card_RefreshUsage));
    public static string Accounts_Card_SonnetOnly => Get(nameof(Accounts_Card_SonnetOnly));
    public static string Accounts_Card_Switch => Get(nameof(Accounts_Card_Switch));
    public static string Accounts_Card_SwitchFailed => Get(nameof(Accounts_Card_SwitchFailed));
    public static string Accounts_Card_SwitchSuccess(object arg0) => Get(nameof(Accounts_Card_SwitchSuccess), arg0);
    public static string Accounts_Card_TokenRefreshFailed => Get(nameof(Accounts_Card_TokenRefreshFailed));
    public static string Accounts_Card_TokenRefreshed => Get(nameof(Accounts_Card_TokenRefreshed));
    public static string Accounts_Card_UsageError => Get(nameof(Accounts_Card_UsageError));
    public static string Accounts_Card_UsageLoadError(object arg0) => Get(nameof(Accounts_Card_UsageLoadError), arg0);
    public static string Accounts_Card_UsageNone => Get(nameof(Accounts_Card_UsageNone));
    public static string Accounts_Desc => Get(nameof(Accounts_Desc));
    public static string Accounts_Dialog_Desc => Get(nameof(Accounts_Dialog_Desc));
    public static string Accounts_Dialog_Detected => Get(nameof(Accounts_Dialog_Detected));
    public static string Accounts_Dialog_EmailNotDetected => Get(nameof(Accounts_Dialog_EmailNotDetected));
    public static string Accounts_Dialog_Failed(object arg0) => Get(nameof(Accounts_Dialog_Failed), arg0);
    public static string Accounts_Dialog_LoggedIn => Get(nameof(Accounts_Dialog_LoggedIn));
    public static string Accounts_Dialog_ProfileName => Get(nameof(Accounts_Dialog_ProfileName));
    public static string Accounts_Dialog_ProfileNameHint => Get(nameof(Accounts_Dialog_ProfileNameHint));
    public static string Accounts_Dialog_ProfileNamePlaceholder => Get(nameof(Accounts_Dialog_ProfileNamePlaceholder));
    public static string Accounts_Dialog_Register => Get(nameof(Accounts_Dialog_Register));
    public static string Accounts_Dialog_Success(object arg0) => Get(nameof(Accounts_Dialog_Success), arg0);
    public static string Accounts_Dialog_Title => Get(nameof(Accounts_Dialog_Title));
    public static string Accounts_Empty => Get(nameof(Accounts_Empty));
    public static string Accounts_EmptyHint => Get(nameof(Accounts_EmptyHint));
    public static string Accounts_RegisterCurrent => Get(nameof(Accounts_RegisterCurrent));
    public static string Accounts_SwitchBlocked => Get(nameof(Accounts_SwitchBlocked));
    public static string Accounts_Title => Get(nameof(Accounts_Title));
    public static string Common_EmptyContent => Get(nameof(Common_EmptyContent));
    public static string Common_Refresh => Get(nameof(Common_Refresh));
    public static string Common_SaveFailed(object arg0) => Get(nameof(Common_SaveFailed), arg0);
    public static string Common_SwitchToEditor => Get(nameof(Common_SwitchToEditor));
    public static string Common_SwitchToPreview => Get(nameof(Common_SwitchToPreview));
    public static string Hooks_AddHook => Get(nameof(Hooks_AddHook));
    public static string Hooks_AgentModelLabel => Get(nameof(Hooks_AgentModelLabel));
    public static string Hooks_AgentPromptLabel => Get(nameof(Hooks_AgentPromptLabel));
    public static string Hooks_AsyncExecution => Get(nameof(Hooks_AsyncExecution));
    public static string Hooks_CommandLabel => Get(nameof(Hooks_CommandLabel));
    public static string Hooks_Delete => Get(nameof(Hooks_Delete));
    public static string Hooks_Deleted => Get(nameof(Hooks_Deleted));
    public static string Hooks_Event => Get(nameof(Hooks_Event));
    public static string Hooks_HandlerType => Get(nameof(Hooks_HandlerType));
    public static string Hooks_HeadersLabel => Get(nameof(Hooks_HeadersLabel));
    public static string Hooks_ModelOptionalLabel => Get(nameof(Hooks_ModelOptionalLabel));
    public static string Hooks_Page_Desc => Get(nameof(Hooks_Page_Desc));
    public static string Hooks_Page_Title => Get(nameof(Hooks_Page_Title));
    public static string Hooks_PromptLabel => Get(nameof(Hooks_PromptLabel));
    public static string Hooks_Saved => Get(nameof(Hooks_Saved));
    public static string Hooks_StatusMessageLabel => Get(nameof(Hooks_StatusMessageLabel));
    public static string Hooks_TimeoutLabel => Get(nameof(Hooks_TimeoutLabel));
    public static string Hooks_TypeAgent => Get(nameof(Hooks_TypeAgent));
    public static string Hooks_TypeCommand => Get(nameof(Hooks_TypeCommand));
    public static string Hooks_TypeHttp => Get(nameof(Hooks_TypeHttp));
    public static string Hooks_TypePrompt => Get(nameof(Hooks_TypePrompt));
    public static string Hooks_UrlLabel => Get(nameof(Hooks_UrlLabel));
    public static string Instructions_NoFile => Get(nameof(Instructions_NoFile));
    public static string Instructions_Page_Desc => Get(nameof(Instructions_Page_Desc));
    public static string Instructions_Page_Title => Get(nameof(Instructions_Page_Title));
    public static string Instructions_ReferencedFiles => Get(nameof(Instructions_ReferencedFiles));
    public static string Instructions_RootClaude => Get(nameof(Instructions_RootClaude));
    public static string Instructions_SaveError(object arg0) => Get(nameof(Instructions_SaveError), arg0);
    public static string Instructions_Saved => Get(nameof(Instructions_Saved));
    public static string Mcp_Card_Command => Get(nameof(Mcp_Card_Command));
    public static string Mcp_Card_Connected => Get(nameof(Mcp_Card_Connected));
    public static string Mcp_Card_Disconnected => Get(nameof(Mcp_Card_Disconnected));
    public static string Mcp_Card_Edit => Get(nameof(Mcp_Card_Edit));
    public static string Mcp_Card_Name => Get(nameof(Mcp_Card_Name));
    public static string Mcp_Card_Remove => Get(nameof(Mcp_Card_Remove));
    public static string Mcp_Card_Status => Get(nameof(Mcp_Card_Status));
    public static string Mcp_Card_Type => Get(nameof(Mcp_Card_Type));
    public static string Mcp_Card_Url => Get(nameof(Mcp_Card_Url));
    public static string Mcp_Editor_CommandLabel => Get(nameof(Mcp_Editor_CommandLabel));
    public static string Mcp_Editor_EnvVarsLabel => Get(nameof(Mcp_Editor_EnvVarsLabel));
    public static string Mcp_Editor_NewServer => Get(nameof(Mcp_Editor_NewServer));
    public static string Mcp_Editor_Save => Get(nameof(Mcp_Editor_Save));
    public static string Mcp_Editor_Title => Get(nameof(Mcp_Editor_Title));
    public static string Mcp_Editor_UrlLabel => Get(nameof(Mcp_Editor_UrlLabel));
    public static string Mcp_List_AddFirst => Get(nameof(Mcp_List_AddFirst));
    public static string Mcp_List_NoServers => Get(nameof(Mcp_List_NoServers));
    public static string Mcp_Page_AddServer => Get(nameof(Mcp_Page_AddServer));
    public static string Mcp_Page_Desc => Get(nameof(Mcp_Page_Desc));
    public static string Mcp_Page_ImportFailed(object arg0) => Get(nameof(Mcp_Page_ImportFailed), arg0);
    public static string Mcp_Page_ImportJson => Get(nameof(Mcp_Page_ImportJson));
    public static string Mcp_Page_ImportSuccess => Get(nameof(Mcp_Page_ImportSuccess));
    public static string Mcp_Page_ServerAdded(object arg0) => Get(nameof(Mcp_Page_ServerAdded), arg0);
    public static string Mcp_Page_ServerRemoved(object arg0) => Get(nameof(Mcp_Page_ServerRemoved), arg0);
    public static string Mcp_Page_ServerUpdated(object arg0) => Get(nameof(Mcp_Page_ServerUpdated), arg0);
    public static string Mcp_Page_Template => Get(nameof(Mcp_Page_Template));
    public static string Mcp_Page_Title => Get(nameof(Mcp_Page_Title));
    public static string Memory_All => Get(nameof(Memory_All));
    public static string Memory_ContentLabel => Get(nameof(Memory_ContentLabel));
    public static string Memory_CreatedModified(object arg0, object arg1) => Get(nameof(Memory_CreatedModified), arg0, arg1);
    public static string Memory_DeleteError(object arg0) => Get(nameof(Memory_DeleteError), arg0);
    public static string Memory_Deleted => Get(nameof(Memory_Deleted));
    public static string Memory_Desc => Get(nameof(Memory_Desc));
    public static string Memory_DescriptionLabel => Get(nameof(Memory_DescriptionLabel));
    public static string Memory_Empty => Get(nameof(Memory_Empty));
    public static string Memory_NameLabel => Get(nameof(Memory_NameLabel));
    public static string Memory_SaveError(object arg0) => Get(nameof(Memory_SaveError), arg0);
    public static string Memory_Saved => Get(nameof(Memory_Saved));
    public static string Memory_SearchPlaceholder => Get(nameof(Memory_SearchPlaceholder));
    public static string Memory_SelectHint => Get(nameof(Memory_SelectHint));
    public static string Memory_Title => Get(nameof(Memory_Title));
    public static string Memory_TypeFeedback => Get(nameof(Memory_TypeFeedback));
    public static string Memory_TypeLabel => Get(nameof(Memory_TypeLabel));
    public static string Memory_TypeOther => Get(nameof(Memory_TypeOther));
    public static string Memory_TypeProject => Get(nameof(Memory_TypeProject));
    public static string Memory_TypeReference => Get(nameof(Memory_TypeReference));
    public static string Memory_TypeUser => Get(nameof(Memory_TypeUser));
    public static string Notifications_BackToApp => Get(nameof(Notifications_BackToApp));
    public static string Notifications_DeletedSession => Get(nameof(Notifications_DeletedSession));
    public static string Notifications_Empty => Get(nameof(Notifications_Empty));
    public static string Notifications_MarkAllRead => Get(nameof(Notifications_MarkAllRead));
    public static string Notifications_Title => Get(nameof(Notifications_Title));
    public static string Onboarding_Feature1 => Get(nameof(Onboarding_Feature1));
    public static string Onboarding_Feature2 => Get(nameof(Onboarding_Feature2));
    public static string Onboarding_Feature3 => Get(nameof(Onboarding_Feature3));
    public static string Onboarding_Layout_Chat => Get(nameof(Onboarding_Layout_Chat));
    public static string Onboarding_Layout_Desc => Get(nameof(Onboarding_Layout_Desc));
    public static string Onboarding_Layout_Panel => Get(nameof(Onboarding_Layout_Panel));
    public static string Onboarding_Layout_Settings => Get(nameof(Onboarding_Layout_Settings));
    public static string Onboarding_Layout_Sidebar => Get(nameof(Onboarding_Layout_Sidebar));
    public static string Onboarding_Layout_Title => Get(nameof(Onboarding_Layout_Title));
    public static string Onboarding_Next => Get(nameof(Onboarding_Next));
    public static string Onboarding_Prev => Get(nameof(Onboarding_Prev));
    public static string Onboarding_Session_Desc => Get(nameof(Onboarding_Session_Desc));
    public static string Onboarding_Session_Step1 => Get(nameof(Onboarding_Session_Step1));
    public static string Onboarding_Session_Step2 => Get(nameof(Onboarding_Session_Step2));
    public static string Onboarding_Session_Step3 => Get(nameof(Onboarding_Session_Step3));
    public static string Onboarding_Session_Title => Get(nameof(Onboarding_Session_Title));
    public static string Onboarding_Skip => Get(nameof(Onboarding_Skip));
    public static string Onboarding_Start => Get(nameof(Onboarding_Start));
    public static string Onboarding_Title => Get(nameof(Onboarding_Title));
    public static string Onboarding_Welcome_Desc => Get(nameof(Onboarding_Welcome_Desc));
    public static string Onboarding_Welcome_Title => Get(nameof(Onboarding_Welcome_Title));
    public static string Onboarding_Workspace_Desc => Get(nameof(Onboarding_Workspace_Desc));
    public static string Onboarding_Workspace_Step1 => Get(nameof(Onboarding_Workspace_Step1));
    public static string Onboarding_Workspace_Step2 => Get(nameof(Onboarding_Workspace_Step2));
    public static string Onboarding_Workspace_Step3 => Get(nameof(Onboarding_Workspace_Step3));
    public static string Onboarding_Workspace_Title => Get(nameof(Onboarding_Workspace_Title));
    public static string Plugins_Activated => Get(nameof(Plugins_Activated));
    public static string Plugins_AddFolderHint => Get(nameof(Plugins_AddFolderHint));
    public static string Plugins_Blocked => Get(nameof(Plugins_Blocked));
    public static string Plugins_Deactivated => Get(nameof(Plugins_Deactivated));
    public static string Plugins_Desc => Get(nameof(Plugins_Desc));
    public static string Plugins_Install => Get(nameof(Plugins_Install));
    public static string Plugins_InstallFailed(object arg0) => Get(nameof(Plugins_InstallFailed), arg0);
    public static string Plugins_InstallSuccess(object arg0) => Get(nameof(Plugins_InstallSuccess), arg0);
    public static string Plugins_Installing => Get(nameof(Plugins_Installing));
    public static string Plugins_Inactive => Get(nameof(Plugins_Inactive));
    public static string Plugins_MarketplaceHint => Get(nameof(Plugins_MarketplaceHint));
    public static string Plugins_NoMarketplace => Get(nameof(Plugins_NoMarketplace));
    public static string Plugins_NoneInstalled => Get(nameof(Plugins_NoneInstalled));
    public static string Plugins_NoneInstalledHint => Get(nameof(Plugins_NoneInstalledHint));
    public static string Plugins_NoResults => Get(nameof(Plugins_NoResults));
    public static string Plugins_OpenDirFailed(object arg0) => Get(nameof(Plugins_OpenDirFailed), arg0);
    public static string Plugins_OpenDirectory => Get(nameof(Plugins_OpenDirectory));
    public static string Plugins_RemoveFailed(object arg0) => Get(nameof(Plugins_RemoveFailed), arg0);
    public static string Plugins_RemoveSuccess(object arg0) => Get(nameof(Plugins_RemoveSuccess), arg0);
    public static string Plugins_SearchInstalled => Get(nameof(Plugins_SearchInstalled));
    public static string Plugins_SearchMarketplace => Get(nameof(Plugins_SearchMarketplace));
    public static string Plugins_StatusChecking => Get(nameof(Plugins_StatusChecking));
    public static string Plugins_StatusError => Get(nameof(Plugins_StatusError));
    public static string Plugins_StatusLoaded => Get(nameof(Plugins_StatusLoaded));
    public static string Plugins_StatusNormal => Get(nameof(Plugins_StatusNormal));
    public static string Plugins_TabInstalled => Get(nameof(Plugins_TabInstalled));
    public static string Plugins_TabMarketplace => Get(nameof(Plugins_TabMarketplace));
    public static string Plugins_Title => Get(nameof(Plugins_Title));
    public static string Plugins_ToggleFailed(object arg0) => Get(nameof(Plugins_ToggleFailed), arg0);
    public static string Plugins_ToggleSuccess(object arg0, object arg1) => Get(nameof(Plugins_ToggleSuccess), arg0, arg1);
    public static string Plugins_Uninstall => Get(nameof(Plugins_Uninstall));
    public static string Plugins_UninstallConfirm(object arg0) => Get(nameof(Plugins_UninstallConfirm), arg0);
    public static string Plugins_UninstallTitle => Get(nameof(Plugins_UninstallTitle));
    public static string Plugins_Update => Get(nameof(Plugins_Update));
    public static string Plugins_UpdateFailed(object arg0) => Get(nameof(Plugins_UpdateFailed), arg0);
    public static string Plugins_UpdateSuccess(object arg0) => Get(nameof(Plugins_UpdateSuccess), arg0);
    public static string Rules_ContentLabel => Get(nameof(Rules_ContentLabel));
    public static string Rules_DeleteConfirm(object arg0) => Get(nameof(Rules_DeleteConfirm), arg0);
    public static string Rules_DeleteError(object arg0) => Get(nameof(Rules_DeleteError), arg0);
    public static string Rules_DeleteTitle => Get(nameof(Rules_DeleteTitle));
    public static string Rules_Deleted => Get(nameof(Rules_Deleted));
    public static string Rules_Desc => Get(nameof(Rules_Desc));
    public static string Rules_Empty => Get(nameof(Rules_Empty));
    public static string Rules_FileName => Get(nameof(Rules_FileName));
    public static string Rules_Name => Get(nameof(Rules_Name));
    public static string Rules_New => Get(nameof(Rules_New));
    public static string Rules_SaveError(object arg0) => Get(nameof(Rules_SaveError), arg0);
    public static string Rules_Saved => Get(nameof(Rules_Saved));
    public static string Rules_SearchPlaceholder => Get(nameof(Rules_SearchPlaceholder));
    public static string Rules_TemplateApplied(object arg0) => Get(nameof(Rules_TemplateApplied), arg0);
    public static string Sessions_CountLabel => Get(nameof(Sessions_CountLabel));
    public static string Sessions_Desc => Get(nameof(Sessions_Desc));
    public static string Sessions_Empty => Get(nameof(Sessions_Empty));
    public static string Sessions_Realtime => Get(nameof(Sessions_Realtime));
    public static string Sessions_Reset => Get(nameof(Sessions_Reset));
    public static string Sessions_Results => Get(nameof(Sessions_Results));
    public static string Sessions_Searching => Get(nameof(Sessions_Searching));
    public static string Sessions_SearchPlaceholder => Get(nameof(Sessions_SearchPlaceholder));
    public static string Sessions_SortBy => Get(nameof(Sessions_SortBy));
    public static string Sessions_Title => Get(nameof(Sessions_Title));
    public static string Sessions_UsageAnalytics => Get(nameof(Sessions_UsageAnalytics));
    public static string Setup_Desc => Get(nameof(Setup_Desc));
    public static string Setup_HowToInstall => Get(nameof(Setup_HowToInstall));
    public static string Setup_Installed => Get(nameof(Setup_Installed));
    public static string Setup_Recheck => Get(nameof(Setup_Recheck));
    public static string Setup_Skip => Get(nameof(Setup_Skip));
    public static string Setup_Start => Get(nameof(Setup_Start));
    public static string Setup_Title => Get(nameof(Setup_Title));
    public static string TemplateGallery_All => Get(nameof(TemplateGallery_All));
    public static string TemplateGallery_Agent => Get(nameof(TemplateGallery_Agent));
    public static string TemplateGallery_Hook => Get(nameof(TemplateGallery_Hook));
    public static string TemplateGallery_Mcp => Get(nameof(TemplateGallery_Mcp));
    public static string TemplateGallery_NoResults => Get(nameof(TemplateGallery_NoResults));
    public static string TemplateGallery_Rules => Get(nameof(TemplateGallery_Rules));
    public static string TemplateGallery_SearchPlaceholder => Get(nameof(TemplateGallery_SearchPlaceholder));
    public static string TemplateGallery_Skill => Get(nameof(TemplateGallery_Skill));
    public static string TemplateGallery_Title => Get(nameof(TemplateGallery_Title));
    public static string Tool_Card_Collapse => Get(nameof(Tool_Card_Collapse));
    public static string Tool_Card_ExpandAll => Get(nameof(Tool_Card_ExpandAll));
    public static string Tool_Card_Input => Get(nameof(Tool_Card_Input));
    public static string Tool_Card_Lines(object arg0) => Get(nameof(Tool_Card_Lines), arg0);
    public static string Tool_Card_Output => Get(nameof(Tool_Card_Output));
    public static string WhatsNew_CurrentVersion => Get(nameof(WhatsNew_CurrentVersion));
    public static string WhatsNew_DateDayBeforeYesterday => Get(nameof(WhatsNew_DateDayBeforeYesterday));
    public static string WhatsNew_DateFormat(object arg0) => Get(nameof(WhatsNew_DateFormat), arg0);
    public static string WhatsNew_DateToday => Get(nameof(WhatsNew_DateToday));
    public static string WhatsNew_DateYesterday => Get(nameof(WhatsNew_DateYesterday));
    public static string WhatsNew_Empty => Get(nameof(WhatsNew_Empty));
    public static string WhatsNew_Title => Get(nameof(WhatsNew_Title));
    public static string WhatsNew_TypeDocs => Get(nameof(WhatsNew_TypeDocs));
    public static string WhatsNew_TypeFeature => Get(nameof(WhatsNew_TypeFeature));
    public static string WhatsNew_TypeFix => Get(nameof(WhatsNew_TypeFix));
    public static string WhatsNew_TypeImprove => Get(nameof(WhatsNew_TypeImprove));
    public static string WhatsNew_TypeOther => Get(nameof(WhatsNew_TypeOther));
    public static string WhatsNew_TypeStyle => Get(nameof(WhatsNew_TypeStyle));
    public static string WhatsNew_TypeTranslation => Get(nameof(WhatsNew_TypeTranslation));

    internal static string Get(string name)
    {
        var value = Rm.GetString(name, CultureInfo.CurrentUICulture);
#if DEBUG
        if (value == null)
            System.Diagnostics.Debug.WriteLine($"[i18n] Missing key: {name}");
#endif
        return value ?? name;
    }

    private static string Get(string name, object arg0)
    {
        return string.Format(Get(name), arg0);
    }

    private static string Get(string name, object arg0, object arg1)
    {
        return string.Format(Get(name), arg0, arg1);
    }

    private static string Get(string name, object arg0, object arg1, object arg2)
    {
        return string.Format(Get(name), arg0, arg1, arg2);
    }
}