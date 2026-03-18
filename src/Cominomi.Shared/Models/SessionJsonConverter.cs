using System.Text.Json;
using System.Text.Json.Serialization;
using Cominomi.Shared.Services;
using Cominomi.Shared.Services.Migration;

namespace Cominomi.Shared.Models;

/// <summary>
/// 기존 플랫 JSON 포맷과 새 중첩 포맷(git/pr) 모두 지원하는 컨버터.
/// 읽기: 플랫(기존) → 중첩(신규) 양쪽 모두 역직렬화 가능.
/// 쓰기: 항상 중첩 포맷으로 직렬화.
/// </summary>
public class SessionJsonConverter : JsonConverter<Session>
{
    public override Session Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var session = new Session();

        // Scalar properties
        if (root.TryGet("id", out var id)) session.Id = id;
        if (root.TryGet("title", out var title)) session.Title = title;
        if (root.TryGet("model", out var model)) session.Model = model;
        if (root.TryGet("workspaceId", out var wsId)) session.WorkspaceId = wsId;
        if (root.TryGet("permissionMode", out var pm)) session.PermissionMode = pm;
        if (root.TryGet("effortLevel", out var el)) session.EffortLevel = el;
        if (root.TryGet("cityName", out var cn)) session.CityName = cn;
        // Error: try new structured "error" object first, then legacy "errorMessage" string
        if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.Object)
        {
            session.Error = JsonSerializer.Deserialize<AppError>(errEl.GetRawText(), options);
        }
        else if (root.TryGet("errorMessage", out var em))
        {
            session.Error = AppError.General(em);
        }
        if (root.TryGet("conversationId", out var cid)) session.ConversationId = cid;
        if (root.TryGet("planFilePath", out var pfp)) session.PlanFilePath = pfp;

        if (root.TryGetProperty("agentType", out var atEl) && atEl.ValueKind == JsonValueKind.String)
        {
            if (Enum.TryParse<AgentType>(atEl.GetString(), true, out var at))
                session.AgentType = at;
        }

        if (root.TryGetProperty("status", out var stEl) && stEl.ValueKind == JsonValueKind.String)
        {
            if (Enum.TryParse<SessionStatus>(stEl.GetString(), true, out var st))
                session.SetInitialStatus(st);
        }

        if (root.TryGetProperty("maxTurns", out var mt) && mt.ValueKind == JsonValueKind.Number)
            session.MaxTurns = mt.GetInt32();
        if (root.TryGetProperty("maxBudgetUsd", out var mb) && mb.ValueKind == JsonValueKind.Number)
            session.MaxBudgetUsd = mb.GetDecimal();
        if (root.TryGetProperty("totalInputTokens", out var tit) && tit.ValueKind == JsonValueKind.Number)
            session.TotalInputTokens = tit.GetInt64();
        if (root.TryGetProperty("totalOutputTokens", out var tot) && tot.ValueKind == JsonValueKind.Number)
            session.TotalOutputTokens = tot.GetInt64();
        if (root.TryGetProperty("planCompleted", out var pc) && pc.ValueKind is JsonValueKind.True or JsonValueKind.False)
            session.PlanCompleted = pc.GetBoolean();

        if (root.TryGetProperty("createdAt", out var ca) && ca.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(ca.GetString(), out var dt)) session.CreatedAt = dt;
        }
        if (root.TryGetProperty("updatedAt", out var ua) && ua.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(ua.GetString(), out var dt)) session.UpdatedAt = dt;
        }

        // Messages (may be empty array in metadata file)
        if (root.TryGetProperty("messages", out var msgsEl) && msgsEl.ValueKind == JsonValueKind.Array)
        {
            session.Messages = JsonSerializer.Deserialize<List<ChatMessage>>(msgsEl.GetRawText(), options) ?? [];
        }

        // Git context: try nested "git" first, then flat properties
        if (root.TryGetProperty("git", out var gitEl) && gitEl.ValueKind == JsonValueKind.Object)
        {
            session.Git = DeserializeGitContext(gitEl, options);
        }
        else
        {
            // Legacy flat format
            session.Git = new GitContext();
            if (root.TryGet("worktreePath", out var wtp)) session.Git.WorktreePath = wtp;
            if (root.TryGet("branchName", out var bn)) session.Git.BranchName = bn;
            if (root.TryGet("baseBranch", out var bb)) session.Git.BaseBranch = bb;
            if (root.TryGetProperty("isLocalDir", out var ild) && ild.ValueKind is JsonValueKind.True or JsonValueKind.False)
                session.Git.IsLocalDir = ild.GetBoolean();
            if (root.TryGetProperty("additionalDirs", out var ad) && ad.ValueKind == JsonValueKind.Array)
                session.Git.AdditionalDirs = JsonSerializer.Deserialize<List<string>>(ad.GetRawText(), options) ?? [];
        }

        // PR context: try nested "pr" first, then flat properties
        if (root.TryGetProperty("pr", out var prEl) && prEl.ValueKind == JsonValueKind.Object)
        {
            session.Pr = DeserializePrContext(prEl, options);
        }
        else
        {
            // Legacy flat format
            session.Pr = new PrContext();
            if (root.TryGet("prUrl", out var pu)) session.Pr.PrUrl = pu;
            if (root.TryGetProperty("prNumber", out var pn) && pn.ValueKind == JsonValueKind.Number)
                session.Pr.PrNumber = pn.GetInt32();
            if (root.TryGetProperty("issueNumber", out var inEl) && inEl.ValueKind == JsonValueKind.Number)
                session.Pr.IssueNumber = inEl.GetInt32();
            if (root.TryGet("issueUrl", out var iu)) session.Pr.IssueUrl = iu;
            if (root.TryGetProperty("conflictFiles", out var cf) && cf.ValueKind == JsonValueKind.Array)
                session.Pr.ConflictFiles = JsonSerializer.Deserialize<List<string>>(cf.GetRawText(), options);
        }

        // AllowedTools / DisallowedTools were on Session, now on Git — but actually they're config, keep checking root
        // These are not Git/PR related, but were on Session. Since we removed them, handle gracefully.
        // Actually they were removed from Session entirely — they don't belong to Git or PR.
        // Let me check if they need to stay somewhere...

        return session;
    }

    public override void Write(Utf8JsonWriter writer, Session value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // Schema version stamp
        var migrator = SchemaMigratorRegistry.GetMigrator<Session>();
        writer.WriteNumber(SchemaVersion.FieldName, migrator?.CurrentVersion ?? 2);

        writer.WriteString("id", value.Id);
        writer.WriteString("title", value.Title);
        writer.WriteString("model", value.Model);
        writer.WriteString("workspaceId", value.WorkspaceId);
        writer.WriteString("permissionMode", value.PermissionMode);
        writer.WriteString("effortLevel", value.EffortLevel);
        writer.WriteString("agentType", value.AgentType.ToString());
        writer.WriteString("cityName", value.CityName);
        writer.WriteString("status", value.Status.ToString());

        if (value.Error != null)
        {
            writer.WritePropertyName("error");
            JsonSerializer.Serialize(writer, value.Error, options);
        }
        if (value.ConversationId != null)
            writer.WriteString("conversationId", value.ConversationId);

        // Messages (written as empty array for metadata; actual messages saved separately)
        writer.WritePropertyName("messages");
        JsonSerializer.Serialize(writer, value.Messages, options);

        // Git context (nested)
        writer.WritePropertyName("git");
        writer.WriteStartObject();
        writer.WriteString("worktreePath", value.Git.WorktreePath);
        writer.WriteString("branchName", value.Git.BranchName);
        writer.WriteString("baseBranch", value.Git.BaseBranch);
        writer.WriteBoolean("isLocalDir", value.Git.IsLocalDir);
        writer.WritePropertyName("additionalDirs");
        JsonSerializer.Serialize(writer, value.Git.AdditionalDirs, options);
        writer.WriteEndObject();

        // PR context (nested)
        writer.WritePropertyName("pr");
        writer.WriteStartObject();
        if (value.Pr.PrUrl != null) writer.WriteString("prUrl", value.Pr.PrUrl);
        if (value.Pr.PrNumber != null) writer.WriteNumber("prNumber", value.Pr.PrNumber.Value);
        if (value.Pr.IssueNumber != null) writer.WriteNumber("issueNumber", value.Pr.IssueNumber.Value);
        if (value.Pr.IssueUrl != null) writer.WriteString("issueUrl", value.Pr.IssueUrl);
        if (value.Pr.ConflictFiles != null)
        {
            writer.WritePropertyName("conflictFiles");
            JsonSerializer.Serialize(writer, value.Pr.ConflictFiles, options);
        }
        writer.WriteEndObject();

        if (value.MaxTurns != null) writer.WriteNumber("maxTurns", value.MaxTurns.Value);
        if (value.MaxBudgetUsd != null) writer.WriteNumber("maxBudgetUsd", value.MaxBudgetUsd.Value);
        writer.WriteNumber("totalInputTokens", value.TotalInputTokens);
        writer.WriteNumber("totalOutputTokens", value.TotalOutputTokens);
        writer.WriteBoolean("planCompleted", value.PlanCompleted);
        if (value.PlanFilePath != null) writer.WriteString("planFilePath", value.PlanFilePath);
        writer.WriteString("createdAt", value.CreatedAt);
        writer.WriteString("updatedAt", value.UpdatedAt);

        writer.WriteEndObject();
    }

    private static GitContext DeserializeGitContext(JsonElement el, JsonSerializerOptions options)
    {
        var git = new GitContext();
        if (el.TryGet("worktreePath", out var wtp)) git.WorktreePath = wtp;
        if (el.TryGet("branchName", out var bn)) git.BranchName = bn;
        if (el.TryGet("baseBranch", out var bb)) git.BaseBranch = bb;
        if (el.TryGetProperty("isLocalDir", out var ild) && ild.ValueKind is JsonValueKind.True or JsonValueKind.False)
            git.IsLocalDir = ild.GetBoolean();
        if (el.TryGetProperty("additionalDirs", out var ad) && ad.ValueKind == JsonValueKind.Array)
            git.AdditionalDirs = JsonSerializer.Deserialize<List<string>>(ad.GetRawText(), options) ?? [];
        return git;
    }

    private static PrContext DeserializePrContext(JsonElement el, JsonSerializerOptions options)
    {
        var pr = new PrContext();
        if (el.TryGet("prUrl", out var pu)) pr.PrUrl = pu;
        if (el.TryGetProperty("prNumber", out var pn) && pn.ValueKind == JsonValueKind.Number)
            pr.PrNumber = pn.GetInt32();
        if (el.TryGetProperty("issueNumber", out var inEl) && inEl.ValueKind == JsonValueKind.Number)
            pr.IssueNumber = inEl.GetInt32();
        if (el.TryGet("issueUrl", out var iu)) pr.IssueUrl = iu;
        if (el.TryGetProperty("conflictFiles", out var cf) && cf.ValueKind == JsonValueKind.Array)
            pr.ConflictFiles = JsonSerializer.Deserialize<List<string>>(cf.GetRawText(), options);
        return pr;
    }
}

internal static class JsonElementExtensions
{
    public static bool TryGet(this JsonElement el, string propertyName, out string value)
    {
        value = "";
        if (el.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? "";
            return true;
        }
        return false;
    }
}
