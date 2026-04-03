using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services;

public class ClaudeAccountService(
    IClaudeCredentialService credentialService,
    IChatState chatState,
    HttpClient httpClient,
    ILogger<ClaudeAccountService> logger) : IClaudeAccountService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private ClaudeAccountStore? _store;

    public event Action? OnAccountsChanged;

    // ── Store persistence ──────────────────────────────────────────────────

    private async Task<ClaudeAccountStore> LoadStoreAsync()
    {
        if (_store != null) return _store;

        if (!File.Exists(AppPaths.AccountsFile))
        {
            _store = new ClaudeAccountStore();
            return _store;
        }

        try
        {
            var json = await File.ReadAllTextAsync(AppPaths.AccountsFile);
            _store = JsonSerializer.Deserialize<ClaudeAccountStore>(json, JsonOpts) ?? new ClaudeAccountStore();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read accounts store, starting fresh");
            _store = new ClaudeAccountStore();
        }

        return _store;
    }

    private async Task SaveStoreAsync(ClaudeAccountStore store)
    {
        var json = JsonSerializer.Serialize(store, JsonOpts);
        await AtomicFileWriter.WriteAsync(AppPaths.AccountsFile, json);
        _store = store;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public async Task<List<ClaudeAccount>> GetAccountsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();
            return [..store.Accounts];
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ClaudeAccount?> GetActiveAccountAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();
            return store.Accounts.FirstOrDefault(a => a.IsActive);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ClaudeAccount> RegisterCurrentAsync(string profileName)
    {
        await _lock.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();

            var configJson = await credentialService.ReadCurrentConfigAsync();
            var credentialsJson = await credentialService.ReadCurrentCredentialsAsync();

            var email = credentialService.ExtractEmail(configJson, credentialsJson) ?? "";
            var uuid = ExtractAccountUuid(configJson) ?? "";

            // "현재 계정"을 등록하는 것이므로 항상 활성 — 기존 활성 계정은 비활성으로
            foreach (var existing in store.Accounts)
                existing.IsActive = false;

            var account = new ClaudeAccount
            {
                ProfileName = profileName,
                EmailAddress = email,
                AccountUuid = uuid,
                IsActive = true,
                LastSwitchedAt = DateTime.UtcNow
            };

            // Backup the current credentials under this new account's ID
            await credentialService.BackupAsync(account.Id, configJson, credentialsJson);

            store.ActiveAccountId = account.Id;
            store.Accounts.Add(account);
            await SaveStoreAsync(store);

            logger.LogInformation("Registered account {Email} as '{Profile}' ({Id})",
                email, profileName, account.Id);

            OnAccountsChanged?.Invoke();
            return account;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAccountAsync(string accountId)
    {
        await _lock.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();
            var account = store.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (account == null) return;

            store.Accounts.Remove(account);
            if (store.ActiveAccountId == accountId)
                store.ActiveAccountId = null;

            await credentialService.DeleteBackupAsync(accountId);
            await SaveStoreAsync(store);

            logger.LogInformation("Removed account {Id}", accountId);
            OnAccountsChanged?.Invoke();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateProfileNameAsync(string accountId, string newName)
    {
        await _lock.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();
            var account = store.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (account == null) return;

            account.ProfileName = newName;
            await SaveStoreAsync(store);
            OnAccountsChanged?.Invoke();
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool CanSwitch() => !chatState.HasAnyStreaming();

    public async Task<bool> SwitchToAsync(string targetAccountId)
    {
        if (!CanSwitch())
        {
            logger.LogWarning("Cannot switch accounts while a session is streaming");
            return false;
        }

        await _lock.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();
            var target = store.Accounts.FirstOrDefault(a => a.Id == targetAccountId);
            if (target == null)
            {
                logger.LogWarning("Account {Id} not found", targetAccountId);
                return false;
            }

            if (target.IsActive)
                return true; // already active, no-op

            // ── Step 2: Backup current account ────────────────────────────
            var currentAccount = store.Accounts.FirstOrDefault(a => a.IsActive);
            string? rollbackConfig = null;
            string? rollbackCreds = null;

            if (currentAccount != null)
            {
                rollbackConfig = await credentialService.ReadCurrentConfigAsync();
                rollbackCreds = await credentialService.ReadCurrentCredentialsAsync();

                await credentialService.BackupAsync(currentAccount.Id, rollbackConfig, rollbackCreds);

                // Accumulate time
                if (currentAccount.LastSwitchedAt.HasValue)
                {
                    var elapsed = (long)(DateTime.UtcNow - currentAccount.LastSwitchedAt.Value).TotalSeconds;
                    currentAccount.TotalActiveSeconds += elapsed;
                }
            }

            // ── Step 3: Load target backup ─────────────────────────────────
            var backup = await credentialService.LoadBackupAsync(targetAccountId);
            if (backup == null)
            {
                logger.LogError("Backup for account {Id} is missing or corrupt — aborting switch", targetAccountId);
                return false;
            }

            // ── Step 3b: Restore target credentials ───────────────────────
            try
            {
                if (backup.ConfigJson != null)
                    await credentialService.WriteConfigAsync(backup.ConfigJson);

                if (backup.CredentialsJson != null)
                    await credentialService.WriteCredentialsAsync(backup.CredentialsJson);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to write credentials for {Id}, attempting rollback", targetAccountId);

                // ── Rollback ───────────────────────────────────────────────
                try
                {
                    if (rollbackCreds != null)
                        await credentialService.WriteCredentialsAsync(rollbackCreds);
                    if (rollbackConfig != null)
                        await credentialService.WriteConfigAsync(rollbackConfig);
                }
                catch (Exception rbEx)
                {
                    logger.LogCritical(rbEx, "Rollback failed — credentials may be in inconsistent state");
                }

                return false;
            }

            // ── Step 5: Update metadata ────────────────────────────────────
            if (currentAccount != null)
            {
                currentAccount.IsActive = false;
            }

            target.IsActive = true;
            target.SwitchCount++;
            target.LastSwitchedAt = DateTime.UtcNow;
            store.ActiveAccountId = targetAccountId;

            await SaveStoreAsync(store);

            logger.LogInformation("Switched to account {Email} ({Id})", target.EmailAddress, targetAccountId);
            OnAccountsChanged?.Invoke();
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Usage API ──────────────────────────────────────────────────────────

    public async Task<AccountUsageInfo?> FetchUsageAsync(string accountId)
    {
        try
        {
            var store = await LoadStoreAsync();
            var account = store.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (account == null) return null;

            // Get access_token from the appropriate backup or live credentials
            var credJson = account.IsActive
                ? await credentialService.ReadCurrentCredentialsAsync()
                : (await credentialService.LoadBackupAsync(accountId))?.CredentialsJson;

            if (credJson == null) return null;

            var accessToken = ExtractToken(credJson, "accessToken", "access_token");
            var refreshToken = ExtractToken(credJson, "refreshToken", "refresh_token");
            if (accessToken == null) return null;

            var (responseJson, newAccessToken) = await FetchUsageWithRefreshAsync(accessToken, refreshToken, accountId);
            if (responseJson == null) return null;

            // If token was refreshed, persist the new token
            if (newAccessToken != null && newAccessToken != accessToken)
            {
                await UpdateStoredTokenAsync(accountId, account.IsActive, credJson, newAccessToken);
            }

            return ParseUsageResponse(accountId, responseJson);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch usage for account {Id}", accountId);
            return null;
        }
    }

    private async Task<(string? json, string? newAccessToken)> FetchUsageWithRefreshAsync(
        string accessToken, string? refreshToken, string accountId)
    {
        var result = await CallUsageApiAsync(accessToken);
        if (result != null) return (result, null);

        // 401 — try to refresh
        if (refreshToken == null) return (null, null);

        logger.LogDebug("Access token expired for {Id}, attempting refresh", accountId);
        var newToken = await RefreshAccessTokenAsync(refreshToken);
        if (newToken == null) return (null, null);

        result = await CallUsageApiAsync(newToken);
        return (result, newToken);
    }

    private async Task<string?> CallUsageApiAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        var response = await httpClient.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string?> RefreshAccessTokenAsync(string refreshToken)
    {
        try
        {
            var body = new FormUrlEncodedContent([
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken),
                new("client_id", "9d1c250a-e61b-44d9-88ed-5944d1962f5e")
            ]);

            var response = await httpClient.PostAsync("https://console.anthropic.com/v1/oauth/token", body);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var token))
                return token.GetString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token refresh failed");
        }

        return null;
    }

    private async Task UpdateStoredTokenAsync(string accountId, bool isActive, string oldCredJson, string newAccessToken)
    {
        try
        {
            var node = JsonNode.Parse(oldCredJson)?.AsObject();
            if (node == null) return;

            if (node.ContainsKey("accessToken")) node["accessToken"] = newAccessToken;
            if (node.ContainsKey("access_token")) node["access_token"] = newAccessToken;

            var updatedJson = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            if (isActive)
                await credentialService.WriteCredentialsAsync(updatedJson);

            // Always update the backup so it stays current
            var backup = await credentialService.LoadBackupAsync(accountId);
            await credentialService.BackupAsync(accountId, backup?.ConfigJson, updatedJson);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist refreshed token for {Id}", accountId);
        }
    }

    private static AccountUsageInfo ParseUsageResponse(string accountId, string json)
    {
        var info = new AccountUsageInfo { AccountId = accountId, RawResponseJson = json };
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // ── 5-hour rate limit ──────────────────────────────────────────
            if (root.TryGetProperty("five_hour", out var fiveHour))
            {
                TryGetDouble(fiveHour, out var u, "utilization");  info.Utilization = u;
                TryGetDouble(fiveHour, out var l, "limit");        info.Limit = l;
                TryGetDouble(fiveHour, out var us, "used");        info.Used = us;
            }
            else
            {
                TryGetDouble(root, out var u, "utilization");  info.Utilization = u;
                TryGetDouble(root, out var l, "limit");        info.Limit = l;
                TryGetDouble(root, out var us, "used");        info.Used = us;
            }

            // ── Current week totals ────────────────────────────────────────
            // Try common key names the API might use
            JsonElement week = default;
            bool hasWeek = root.TryGetProperty("current_week", out week)
                        || root.TryGetProperty("week", out week)
                        || root.TryGetProperty("weekly", out week);

            if (hasWeek && week.ValueKind == JsonValueKind.Object)
            {
                TryGetLong(week, out var wt, "total_tokens", "tokens", "input_tokens");
                TryGetLong(week, out var wr, "total_requests", "requests", "count");
                info.WeeklyAllTokens = wt;
                info.WeeklyAllRequests = wr;

                // by_model array: [{ "model": "claude-sonnet-...", "tokens": 123, "requests": 4 }]
                if (week.TryGetProperty("by_model", out var byModel)
                    && byModel.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in byModel.EnumerateArray())
                    {
                        if (!entry.TryGetProperty("model", out var modelProp)) continue;
                        var modelName = modelProp.GetString() ?? "";
                        if (!modelName.Contains("sonnet", StringComparison.OrdinalIgnoreCase)) continue;

                        TryGetLong(entry, out var st, "tokens", "input_tokens", "total_tokens");
                        TryGetLong(entry, out var sr, "requests", "count", "total_requests");
                        info.WeeklySonnetTokens = (info.WeeklySonnetTokens ?? 0) + (st ?? 0);
                        info.WeeklySonnetRequests = (info.WeeklySonnetRequests ?? 0) + (sr ?? 0);
                    }
                }
            }
        }
        catch { /* raw JSON still captured */ }

        return info;
    }

    private static void TryGetDouble(JsonElement el, out double? result, params string[] keys)
    {
        result = null;
        foreach (var k in keys)
            if (el.TryGetProperty(k, out var v) &&
                (v.ValueKind == JsonValueKind.Number) && v.TryGetDouble(out var d))
            { result = d; return; }
    }

    private static void TryGetLong(JsonElement el, out long? result, params string[] keys)
    {
        result = null;
        foreach (var k in keys)
            if (el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number)
            { result = v.GetInt64(); return; }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string? ExtractAccountUuid(string? configJson)
    {
        if (configJson == null) return null;
        try
        {
            var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty("oauthAccount", out var oauth) &&
                oauth.TryGetProperty("accountUuid", out var uuid))
                return uuid.GetString();
        }
        catch { /* ignore */ }

        return null;
    }

    /// <summary>
    ///     Searches for a token key at the root level, then inside any nested object
    ///     (e.g. Windows stores credentials under "claudeAiOauth": { "accessToken": "..." }).
    /// </summary>
    private static string? ExtractToken(string credJson, params string[] keys)
    {
        try
        {
            var doc = JsonDocument.Parse(credJson);
            var root = doc.RootElement;

            // 1. Check top-level keys
            foreach (var key in keys)
                if (root.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
                    return val.GetString();

            // 2. Check one level deep (handles "claudeAiOauth": { "accessToken": "..." })
            foreach (var child in root.EnumerateObject())
            {
                if (child.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var key in keys)
                    if (child.Value.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
                        return val.GetString();
            }
        }
        catch { /* ignore */ }

        return null;
    }

    // ── Drift detection ────────────────────────────────────────────────────

    public async Task SyncActiveAccountAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();
            if (store.Accounts.Count == 0) return;

            // Read live state to find which account is actually logged in
            var liveConfig = await credentialService.ReadCurrentConfigAsync();
            var liveUuid = ExtractAccountUuid(liveConfig);
            var liveEmail = credentialService.ExtractEmail(liveConfig, null);

            // Find the account that matches the live state
            ClaudeAccount? matched = null;
            if (!string.IsNullOrEmpty(liveUuid))
                matched = store.Accounts.FirstOrDefault(a => a.AccountUuid == liveUuid);
            if (matched == null && !string.IsNullOrEmpty(liveEmail))
                matched = store.Accounts.FirstOrDefault(a => a.EmailAddress == liveEmail);

            if (matched == null) return; // unknown state — don't touch flags

            var currentlyActive = store.Accounts.FirstOrDefault(a => a.IsActive);
            if (currentlyActive?.Id == matched.Id) return; // already in sync

            // Update flags
            foreach (var a in store.Accounts) a.IsActive = false;
            matched.IsActive = true;
            matched.LastSwitchedAt ??= DateTime.UtcNow;
            store.ActiveAccountId = matched.Id;

            await SaveStoreAsync(store);
            logger.LogInformation("Synced active account to {Email} ({Id})", matched.EmailAddress, matched.Id);
            OnAccountsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SyncActiveAccount failed — ignoring");
        }
        finally
        {
            _lock.Release();
        }
    }
}
