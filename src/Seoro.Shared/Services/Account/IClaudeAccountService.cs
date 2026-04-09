
namespace Seoro.Shared.Services.Account;

public interface IClaudeAccountService
{
    event Action? OnAccountsChanged;

    Task<List<ClaudeAccount>> GetAccountsAsync();
    Task<ClaudeAccount?> GetActiveAccountAsync();

    /// <summary>Reads the current Claude CLI auth state and registers it as a new account.</summary>
    Task<ClaudeAccount> RegisterCurrentAsync(string profileName);

    Task RemoveAccountAsync(string accountId);
    Task UpdateProfileNameAsync(string accountId, string newName);

    /// <summary>Switches the active Claude CLI credentials to the specified account. Returns false on failure.</summary>
    Task<bool> SwitchToAsync(string accountId);

    /// <summary>Returns false when a streaming session is active (switch would be unsafe).</summary>
    bool CanSwitch();

    /// <summary>Fetches API usage for an account. Returns null on any error.</summary>
    Task<AccountUsageInfo?> FetchUsageAsync(string accountId);

    /// <summary>
    ///     Explicitly refreshes the OAuth access token using the stored refresh token.
    ///     Returns true if the token was successfully refreshed and persisted.
    /// </summary>
    Task<bool> RefreshTokenAsync(string accountId);

    /// <summary>
    ///     Backs up the currently active account's live credentials, then clears
    ///     the live credential state so CLI is in logged-out mode.
    ///     Returns the ID of the backed-up account (for restore on cancel), or null if none was active.
    /// </summary>
    Task<string?> PrepareForNewLoginAsync();

    /// <summary>
    ///     Restores the previously active account's credentials from backup.
    ///     Called when the user cancels the "add account" flow.
    /// </summary>
    Task RestoreAfterCancelAsync(string accountId);

    /// <summary>
    ///     Compares the live ~/.claude/ state against stored accounts and updates IsActive flags.
    ///     Call on page entry to catch external logins / logouts.
    /// </summary>
    Task SyncActiveAccountAsync();

}
