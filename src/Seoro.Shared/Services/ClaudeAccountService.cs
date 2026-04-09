using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Seoro.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Seoro.Shared.Services;

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
            logger.LogWarning(ex, "계정 저장소 읽기 실패, 새로 시작");
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

            logger.LogInformation("계정 {Email}을 '{Profile}'로 등록됨 ({Id})",
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

            logger.LogInformation("계정 {Id} 제거됨", accountId);
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
            logger.LogWarning("세션 스트리밍 중에는 계정을 전환할 수 없음");
            return false;
        }

        await _lock.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();
            var target = store.Accounts.FirstOrDefault(a => a.Id == targetAccountId);
            if (target == null)
            {
                logger.LogWarning("계정 {Id}을 찾을 수 없음", targetAccountId);
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
                logger.LogError("계정 {Id}의 백업이 누락되었거나 손상됨 - 전환 중단", targetAccountId);
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
                logger.LogError(ex, "계정 {Id}의 자격증명 쓰기 실패, 롤백 시도", targetAccountId);

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
                    logger.LogCritical(rbEx, "롤백 실패 - 자격증명이 불일치 상태일 수 있음");
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

            logger.LogInformation("계정 {Email} ({Id})로 전환됨", target.EmailAddress, targetAccountId);
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

            var (responseJson, newAccessToken, newRefreshToken) = await FetchUsageWithRefreshAsync(accessToken, refreshToken, accountId);
            if (responseJson == null) return null;

            // If token was refreshed, persist both new tokens
            if (newAccessToken != null && newAccessToken != accessToken)
            {
                await UpdateStoredTokenAsync(accountId, account.IsActive, credJson, newAccessToken, newRefreshToken);
            }

            return ParseUsageResponse(accountId, responseJson);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "계정 {Id}의 사용량 가져오기 실패", accountId);
            return null;
        }
    }

    private async Task<(string? json, string? newAccessToken, string? newRefreshToken)> FetchUsageWithRefreshAsync(
        string accessToken, string? refreshToken, string accountId)
    {
        var result = await CallUsageApiAsync(accessToken);
        if (result != null) return (result, null, null);

        // 401 — try to refresh
        if (refreshToken == null) return (null, null, null);

        logger.LogDebug("계정 {Id}의 접근 토큰 만료, 갱신 시도", accountId);
        var (newAccessToken, newRefreshToken) = await RefreshAccessTokenAsync(refreshToken);
        if (newAccessToken == null) return (null, null, null);

        result = await CallUsageApiAsync(newAccessToken);
        return (result, newAccessToken, newRefreshToken);
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

    private async Task<(string? accessToken, string? refreshToken)> RefreshAccessTokenAsync(string refreshToken)
    {
        try
        {
            var body = new FormUrlEncodedContent([
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken),
                new("client_id", "9d1c250a-e61b-44d9-88ed-5944d1962f5e")
            ]);

            var response = await httpClient.PostAsync("https://console.anthropic.com/v1/oauth/token", body);
            if (!response.IsSuccessStatusCode) return (null, null);

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            string? newAccessToken = null;
            string? newRefreshToken = null;
            if (doc.RootElement.TryGetProperty("access_token", out var at))
                newAccessToken = at.GetString();
            if (doc.RootElement.TryGetProperty("refresh_token", out var rt))
                newRefreshToken = rt.GetString();
            return (newAccessToken, newRefreshToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "토큰 갱신 실패");
        }

        return (null, null);
    }

    private async Task UpdateStoredTokenAsync(string accountId, bool isActive, string oldCredJson, string newAccessToken, string? newRefreshToken = null)
    {
        try
        {
            var node = JsonNode.Parse(oldCredJson)?.AsObject();
            if (node == null) return;

            // Update top-level keys
            var updated = false;
            if (node.ContainsKey("accessToken"))  { node["accessToken"]  = newAccessToken; updated = true; }
            if (node.ContainsKey("access_token")) { node["access_token"] = newAccessToken; updated = true; }
            if (newRefreshToken != null)
            {
                if (node.ContainsKey("refreshToken"))  node["refreshToken"]  = newRefreshToken;
                if (node.ContainsKey("refresh_token")) node["refresh_token"] = newRefreshToken;
            }

            // Update nested keys (e.g. Windows: "claudeAiOauth": { "accessToken": "..." })
            if (!updated)
            {
                foreach (var child in node)
                {
                    if (child.Value is not JsonObject nested) continue;
                    if (nested.ContainsKey("accessToken"))  { nested["accessToken"]  = newAccessToken; updated = true; }
                    if (nested.ContainsKey("access_token")) { nested["access_token"] = newAccessToken; updated = true; }
                    if (newRefreshToken != null)
                    {
                        if (nested.ContainsKey("refreshToken"))  nested["refreshToken"]  = newRefreshToken;
                        if (nested.ContainsKey("refresh_token")) nested["refresh_token"] = newRefreshToken;
                    }
                    if (updated) break;
                }
            }

            if (!updated)
            {
                logger.LogWarning("계정 {Id}의 자격증명 JSON에서 accessToken 필드를 찾을 수 없음", accountId);
                return;
            }

            var updatedJson = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            if (isActive)
                await credentialService.WriteCredentialsAsync(updatedJson);

            // Always update the backup so it stays current
            var backup = await credentialService.LoadBackupAsync(accountId);
            await credentialService.BackupAsync(accountId, backup?.ConfigJson, updatedJson);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "계정 {Id}의 갱신된 토큰 저장 실패", accountId);
        }
    }

    private static AccountUsageInfo ParseUsageResponse(string accountId, string json)
    {
        var info = new AccountUsageInfo { AccountId = accountId, RawResponseJson = json };
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            info.FiveHour       = ParseBucket(root, "five_hour");
            info.SevenDayAll    = ParseBucket(root, "seven_day");
            info.SevenDaySonnet = ParseBucket(root, "seven_day_sonnet");
        }
        catch { /* raw JSON still captured */ }

        return info;
    }

    private static UsageBucket? ParseBucket(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var el)
            || el.ValueKind != JsonValueKind.Object)
            return null;

        var bucket = new UsageBucket();

        if (el.TryGetProperty("utilization", out var u)
            && u.ValueKind == JsonValueKind.Number
            && u.TryGetDouble(out var utilVal))
        {
            bucket.Utilization = utilVal;
        }

        if (el.TryGetProperty("resets_at", out var r)
            && r.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(r.GetString(), out var resetVal))
        {
            bucket.ResetsAt = resetVal;
        }

        return bucket;
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

    public async Task<bool> RefreshTokenAsync(string accountId)
    {
        try
        {
            var store = await LoadStoreAsync();
            var account = store.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (account == null) return false;

            var credJson = account.IsActive
                ? await credentialService.ReadCurrentCredentialsAsync()
                : (await credentialService.LoadBackupAsync(accountId))?.CredentialsJson;

            if (credJson == null) return false;

            var refreshToken = ExtractToken(credJson, "refreshToken", "refresh_token");
            if (refreshToken == null)
            {
                logger.LogWarning("계정 {Id}의 갱신 토큰을 찾을 수 없음", accountId);
                return false;
            }

            var (newAccessToken, newRefreshToken) = await RefreshAccessTokenAsync(refreshToken);
            if (newAccessToken == null) return false;

            await UpdateStoredTokenAsync(accountId, account.IsActive, credJson, newAccessToken, newRefreshToken);
            logger.LogInformation("계정 {Id}의 토큰을 수동으로 갱신함", accountId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "계정 {Id}의 수동 토큰 갱신 실패", accountId);
            return false;
        }
    }

    // ── New-account flow ────────────────────────────────────────────────────

    public async Task<string?> PrepareForNewLoginAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();
            var active = store.Accounts.FirstOrDefault(a => a.IsActive);

            if (active != null)
            {
                // Backup live credentials (captures any CLI-refreshed tokens)
                var configJson = await credentialService.ReadCurrentConfigAsync();
                var credentialsJson = await credentialService.ReadCurrentCredentialsAsync();
                await credentialService.BackupAsync(active.Id, configJson, credentialsJson);

                // Accumulate active time
                if (active.LastSwitchedAt.HasValue)
                {
                    var elapsed = (long)(DateTime.UtcNow - active.LastSwitchedAt.Value).TotalSeconds;
                    active.TotalActiveSeconds += elapsed;
                }

                active.IsActive = false;
                store.ActiveAccountId = null;
                await SaveStoreAsync(store);
            }

            // Clear live credentials → CLI sees logged-out state
            await credentialService.ClearCredentialsAsync();
            await credentialService.ClearConfigOAuthAsync();

            logger.LogInformation("새 로그인 준비 완료 (백업: {Id})", active?.Id ?? "none");
            return active?.Id;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RestoreAfterCancelAsync(string accountId)
    {
        await _lock.WaitAsync();
        try
        {
            var backup = await credentialService.LoadBackupAsync(accountId);
            if (backup == null)
            {
                logger.LogWarning("계정 {Id}의 백업을 찾을 수 없음 - 복원 불가", accountId);
                return;
            }

            if (backup.ConfigJson != null)
                await credentialService.WriteConfigAsync(backup.ConfigJson);
            if (backup.CredentialsJson != null)
                await credentialService.WriteCredentialsAsync(backup.CredentialsJson);

            // Re-activate the account
            var store = await LoadStoreAsync();
            var account = store.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (account != null)
            {
                foreach (var a in store.Accounts) a.IsActive = false;
                account.IsActive = true;
                account.LastSwitchedAt = DateTime.UtcNow;
                store.ActiveAccountId = accountId;
                await SaveStoreAsync(store);
            }

            logger.LogInformation("취소 후 계정 {Id} 복원됨", accountId);
            OnAccountsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "취소 후 계정 {Id} 복원 실패", accountId);
        }
        finally
        {
            _lock.Release();
        }
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
            logger.LogInformation("활성 계정을 {Email} ({Id})로 동기화됨", matched.EmailAddress, matched.Id);
            OnAccountsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SyncActiveAccount 실패 - 무시");
        }
        finally
        {
            _lock.Release();
        }
    }
}
