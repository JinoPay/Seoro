using System.Text.Json;
using System.Text.Json.Serialization;
using Seoro.Shared.Services.Migration;

namespace Seoro.Shared.Models.Sessions;

/// <summary>
///     기존 플랫 JSON 포맷과 새 중첩 포맷(git) 모두 지원하는 컨버터.
///     읽기: 플랫(기존) → 중첩(신규) 양쪽 모두 역직렬화 가능.
///     쓰기: 항상 중첩 포맷으로 직렬화.
/// </summary>
public class SessionJsonConverter : JsonConverter<Session>
{
    public override Session Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        // Extract enum values
        var agentType = AgentType.Code;
        if (root.TryGetProperty("agentType", out var atEl) && atEl.ValueKind == JsonValueKind.String)
            if (Enum.TryParse<AgentType>(atEl.GetString(), true, out var at))
                agentType = at;

        int? maxTurns = null;
        if (root.TryGetProperty("maxTurns", out var mt) && mt.ValueKind == JsonValueKind.Number)
            maxTurns = mt.GetInt32();

        decimal? maxBudgetUsd = null;
        if (root.TryGetProperty("maxBudgetUsd", out var mb) && mb.ValueKind == JsonValueKind.Number)
            maxBudgetUsd = mb.GetDecimal();

        var createdAt = DateTime.UtcNow;
        if (root.TryGetProperty("createdAt", out var ca) && ca.ValueKind == JsonValueKind.String)
            if (DateTime.TryParse(ca.GetString(), out var dt))
                createdAt = dt;

        // Git context: try nested "git" first, then flat properties
        GitContext git;
        if (root.TryGetProperty("git", out var gitEl) && gitEl.ValueKind == JsonValueKind.Object)
        {
            git = DeserializeGitContext(gitEl, options);
        }
        else
        {
            // Legacy flat format
            git = new GitContext();
            if (root.TryGet("worktreePath", out var wtp)) git.WorktreePath = wtp;
            if (root.TryGet("branchName", out var bn)) git.BranchName = bn;
            if (root.TryGet("baseBranch", out var bb)) git.BaseBranch = bb;
            if (root.TryGet("baseCommit", out var bc)) git.BaseCommit = bc;
            if (root.TryGetProperty("isLocalDir", out var ild) &&
                ild.ValueKind is JsonValueKind.True or JsonValueKind.False)
                git.IsLocalDir = ild.GetBoolean();
            if (root.TryGetProperty("additionalDirs", out var ad) && ad.ValueKind == JsonValueKind.Array)
                git.AdditionalDirs = JsonSerializer.Deserialize<List<string>>(ad.GetRawText(), options) ?? [];
        }

        // Messages (may be empty array in metadata file)
        List<ChatMessage> messages = [];
        if (root.TryGetProperty("messages", out var msgsEl) && msgsEl.ValueKind == JsonValueKind.Array)
            messages = JsonSerializer.Deserialize<List<ChatMessage>>(msgsEl.GetRawText(), options) ?? [];

        // Create session — init properties set via object initializer
        var session = new Session
        {
            Id = root.TryGet("id", out var id) ? id : Guid.NewGuid().ToString(),
            WorkspaceId = root.TryGet("workspaceId", out var wsId) ? wsId : "default",
            CityName = root.TryGet("cityName", out var cn) ? cn : "",
            AgentType = agentType,
            MaxTurns = maxTurns,
            MaxBudgetUsd = maxBudgetUsd,
            CreatedAt = createdAt,
            Git = git,
            Messages = messages,
            Provider = root.TryGet("provider", out var prov) ? prov : "claude"
        };

        // Mutable properties — set after construction
        if (root.TryGet("title", out var title)) session.Title = title;
        if (root.TryGet("model", out var model)) session.Model = model;
        if (root.TryGet("permissionMode", out var pm)) session.PermissionMode = pm;
        if (root.TryGet("effortLevel", out var el)) session.EffortLevel = el;

        // Error: try new structured "error" object first, then legacy "errorMessage" string
        if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.Object)
            session.Error = JsonSerializer.Deserialize<AppError>(errEl.GetRawText(), options);
        else if (root.TryGet("errorMessage", out var em))
            session.Error = AppError.General(em);

        if (root.TryGet("conversationId", out var cid)) session.ConversationId = cid;
        if (root.TryGet("planFilePath", out var pfp)) session.PlanFilePath = pfp;
        if (root.TryGet("planContent", out var pcnt)) session.PlanContent = pcnt;
        if (root.TryGet("draftInputText", out var dit)) session.DraftInputText = dit;
        if (root.TryGetProperty("draftAttachments", out var da) && da.ValueKind == JsonValueKind.Array)
            session.DraftAttachments = JsonSerializer.Deserialize<List<PendingAttachment>>(da.GetRawText(), options) ?? [];

        if (root.TryGetProperty("status", out var stEl) && stEl.ValueKind == JsonValueKind.String)
        {
            var statusStr = stEl.GetString();
            if (Enum.TryParse<SessionStatus>(statusStr, true, out var st))
                session.SetInitialStatus(st);
            else if (statusStr is "Pushed" or "PrOpen" or "ConflictDetected" or "Merged")
                session.SetInitialStatus(SessionStatus.Ready);
        }

        if (root.TryGetProperty("totalInputTokens", out var tit) && tit.ValueKind == JsonValueKind.Number)
            session.TotalInputTokens = tit.GetInt64();
        if (root.TryGetProperty("totalOutputTokens", out var tot) && tot.ValueKind == JsonValueKind.Number)
            session.TotalOutputTokens = tot.GetInt64();
        if (root.TryGetProperty("titleLocked", out var tl) && tl.ValueKind is JsonValueKind.True or JsonValueKind.False)
            session.TitleLocked = tl.GetBoolean();
        if (root.TryGetProperty("planCompleted", out var pc) &&
            pc.ValueKind is JsonValueKind.True or JsonValueKind.False)
            session.PlanCompleted = pc.GetBoolean();
        if (root.TryGetProperty("pendingAskUserQuestionInput", out var pauq) && pauq.ValueKind == JsonValueKind.String)
            session.PendingAskUserQuestionInput = pauq.GetString();

        if (root.TryGetProperty("updatedAt", out var ua) && ua.ValueKind == JsonValueKind.String)
            if (DateTime.TryParse(ua.GetString(), out var dt))
                session.UpdatedAt = dt;

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

        writer.WriteString("provider", value.Provider);

        // Messages (written as empty array for metadata; actual messages saved separately)
        writer.WritePropertyName("messages");
        JsonSerializer.Serialize(writer, value.Messages, options);

        // Git context (nested)
        writer.WritePropertyName("git");
        writer.WriteStartObject();
        writer.WriteString("worktreePath", value.Git.WorktreePath);
        writer.WriteString("branchName", value.Git.BranchName);
        writer.WriteString("baseBranch", value.Git.BaseBranch);
        writer.WriteString("baseCommit", value.Git.BaseCommit);
        writer.WriteBoolean("isLocalDir", value.Git.IsLocalDir);
        writer.WritePropertyName("additionalDirs");
        JsonSerializer.Serialize(writer, value.Git.AdditionalDirs, options);
        if (!string.IsNullOrEmpty(value.Git.LastPrUrl))
            writer.WriteString("lastPrUrl", value.Git.LastPrUrl);
        if (value.Git.TrackedPr != null)
        {
            writer.WritePropertyName("trackedPr");
            JsonSerializer.Serialize(writer, value.Git.TrackedPr, options);
        }
        writer.WriteEndObject();

        if (value.MaxTurns != null) writer.WriteNumber("maxTurns", value.MaxTurns.Value);
        if (value.MaxBudgetUsd != null) writer.WriteNumber("maxBudgetUsd", value.MaxBudgetUsd.Value);
        writer.WriteNumber("totalInputTokens", value.TotalInputTokens);
        writer.WriteNumber("totalOutputTokens", value.TotalOutputTokens);
        writer.WriteBoolean("titleLocked", value.TitleLocked);
        writer.WriteBoolean("planCompleted", value.PlanCompleted);
        if (value.PlanFilePath != null) writer.WriteString("planFilePath", value.PlanFilePath);
        if (value.PlanContent != null) writer.WriteString("planContent", value.PlanContent);
        if (!string.IsNullOrEmpty(value.DraftInputText))
            writer.WriteString("draftInputText", value.DraftInputText);
        if (value.DraftAttachments.Count > 0)
        {
            writer.WritePropertyName("draftAttachments");
            JsonSerializer.Serialize(writer, value.DraftAttachments, options);
        }
        if (value.PendingAskUserQuestionInput != null)
            writer.WriteString("pendingAskUserQuestionInput", value.PendingAskUserQuestionInput);
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
        if (el.TryGet("baseCommit", out var bc)) git.BaseCommit = bc;
        if (el.TryGetProperty("isLocalDir", out var ild) && ild.ValueKind is JsonValueKind.True or JsonValueKind.False)
            git.IsLocalDir = ild.GetBoolean();
        if (el.TryGetProperty("additionalDirs", out var ad) && ad.ValueKind == JsonValueKind.Array)
            git.AdditionalDirs = JsonSerializer.Deserialize<List<string>>(ad.GetRawText(), options) ?? [];
        if (el.TryGetProperty("lastPrUrl", out var lpu) && lpu.ValueKind == JsonValueKind.String)
            git.LastPrUrl = lpu.GetString();
        if (el.TryGetProperty("trackedPr", out var tp) && tp.ValueKind == JsonValueKind.Object)
            git.TrackedPr = JsonSerializer.Deserialize<TrackedPullRequest>(tp.GetRawText(), options);
        else if (!string.IsNullOrWhiteSpace(git.LastPrUrl))
            git.TrackedPr = new TrackedPullRequest { Url = git.LastPrUrl! };
        return git;
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
