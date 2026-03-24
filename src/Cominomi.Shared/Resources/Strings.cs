using System.Globalization;
using System.Resources;

namespace Cominomi.Shared.Resources;

/// <summary>
/// Strongly-typed accessor for localized strings from Strings.resx.
/// Default culture is Korean (ko). Add satellite assemblies for other cultures.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Rm =
        new("Cominomi.Shared.Resources.Strings", typeof(Strings).Assembly);

    private static string Get(string name) => Rm.GetString(name, CultureInfo.CurrentUICulture) ?? name;
    private static string Get(string name, object arg0) => string.Format(Get(name), arg0);
    private static string Get(string name, object arg0, object arg1) => string.Format(Get(name), arg0, arg1);

    // ── Snackbar ──

    public static string Snackbar_PushSuccess(string branchName) => Get(nameof(Snackbar_PushSuccess), branchName);
    public static string Snackbar_PushError(string error) => Get(nameof(Snackbar_PushError), error);
    public static string Snackbar_PrCreated(int prNumber) => Get(nameof(Snackbar_PrCreated), prNumber);
    public static string Snackbar_PrCreateError(string error) => Get(nameof(Snackbar_PrCreateError), error);
    public static string Snackbar_MergeSuccess(string branchName) => Get(nameof(Snackbar_MergeSuccess), branchName);
    public static string Snackbar_MergeError(string error) => Get(nameof(Snackbar_MergeError), error);
    public static string Snackbar_WorkspaceCreated(string name) => Get(nameof(Snackbar_WorkspaceCreated), name);
    public static string Snackbar_SettingsSaved => Get(nameof(Snackbar_SettingsSaved));
    public static string Snackbar_SessionDeleted => Get(nameof(Snackbar_SessionDeleted));
    public static string Snackbar_ConflictDetected(string branchName) => Get(nameof(Snackbar_ConflictDetected), branchName);
    public static string Snackbar_StreamingError(string error) => Get(nameof(Snackbar_StreamingError), error);
    public static string Snackbar_IssueLinked(int number) => Get(nameof(Snackbar_IssueLinked), number);
    public static string Snackbar_IssueCreated(int number) => Get(nameof(Snackbar_IssueCreated), number);
    public static string Snackbar_ClaudeUpdateRequired(string current, string required) => Get(nameof(Snackbar_ClaudeUpdateRequired), current, required);
    public static string Snackbar_AppUpdateAvailable(string version) => Get(nameof(Snackbar_AppUpdateAvailable), version);
    public static string Snackbar_AppUpdateReady => Get(nameof(Snackbar_AppUpdateReady));
    public static string Snackbar_AppUpdateAction => Get(nameof(Snackbar_AppUpdateAction));
    public static string Snackbar_AppUpdateRestartAction => Get(nameof(Snackbar_AppUpdateRestartAction));

    // ── Tool Display ──

    public static string Tool_FilesRead(int count) => Get(nameof(Tool_FilesRead), count);
    public static string Tool_FileWrittenSingle => Get(nameof(Tool_FileWrittenSingle));
    public static string Tool_FileWrittenMultiple(int count) => Get(nameof(Tool_FileWrittenMultiple), count);
    public static string Tool_FileEditedSingle => Get(nameof(Tool_FileEditedSingle));
    public static string Tool_FileEditedMultiple(int count) => Get(nameof(Tool_FileEditedMultiple), count);
    public static string Tool_GrepSingle => Get(nameof(Tool_GrepSingle));
    public static string Tool_GrepMultiple(int count) => Get(nameof(Tool_GrepMultiple), count);
    public static string Tool_GlobDone => Get(nameof(Tool_GlobDone));
    public static string Tool_BashSingle => Get(nameof(Tool_BashSingle));
    public static string Tool_BashMultiple(int count) => Get(nameof(Tool_BashMultiple), count);
    public static string Tool_AgentSingle => Get(nameof(Tool_AgentSingle));
    public static string Tool_AgentMultiple(int count) => Get(nameof(Tool_AgentMultiple), count);
    public static string Tool_WebFetchSingle => Get(nameof(Tool_WebFetchSingle));
    public static string Tool_WebFetchMultiple(int count) => Get(nameof(Tool_WebFetchMultiple), count);
    public static string Tool_WebSearchSingle => Get(nameof(Tool_WebSearchSingle));
    public static string Tool_WebSearchMultiple(int count) => Get(nameof(Tool_WebSearchMultiple), count);
    public static string Tool_NotebookSingle => Get(nameof(Tool_NotebookSingle));
    public static string Tool_NotebookMultiple(int count) => Get(nameof(Tool_NotebookMultiple), count);
    public static string Tool_TodoWriteDone => Get(nameof(Tool_TodoWriteDone));
    public static string Tool_DefaultMultiple(string name, int count) => Get(nameof(Tool_DefaultMultiple), name, count);
    public static string Tool_ReadHint(int lineCount) => Get(nameof(Tool_ReadHint), lineCount);
    public static string Tool_GrepHint(int lineCount) => Get(nameof(Tool_GrepHint), lineCount);
    public static string Tool_GlobHint(int lineCount) => Get(nameof(Tool_GlobHint), lineCount);

}
