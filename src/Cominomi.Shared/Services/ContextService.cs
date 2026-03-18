using System.Text;
using Cominomi.Shared;
using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public class ContextService : IContextService
{
    private const string ContextDir = ".context";
    private const string NotesFile = "notes.md";
    private const string TodosFile = "todos.md";
    private const string PlansDir = "plans";
    private const string AttachmentsDir = "attachments";

    public async Task<ContextInfo> LoadContextAsync(string worktreePath)
    {
        var contextPath = Path.Combine(worktreePath, ContextDir);
        var info = new ContextInfo();

        var notesPath = Path.Combine(contextPath, NotesFile);
        if (File.Exists(notesPath))
            info.Notes = await File.ReadAllTextAsync(notesPath);

        var todosPath = Path.Combine(contextPath, TodosFile);
        if (File.Exists(todosPath))
            info.Todos = await File.ReadAllTextAsync(todosPath);

        info.Plans = await GetPlansAsync(worktreePath);

        return info;
    }

    public async Task SaveNotesAsync(string worktreePath, string content)
    {
        Guard.NotNullOrWhiteSpace(worktreePath, nameof(worktreePath));
        Guard.NotNull(content, nameof(content));

        await EnsureContextDirectoryAsync(worktreePath);
        var path = Path.Combine(worktreePath, ContextDir, NotesFile);
        await AtomicFileWriter.WriteAsync(path, content);
    }

    public async Task SaveTodosAsync(string worktreePath, string content)
    {
        Guard.NotNullOrWhiteSpace(worktreePath, nameof(worktreePath));
        Guard.NotNull(content, nameof(content));

        await EnsureContextDirectoryAsync(worktreePath);
        var path = Path.Combine(worktreePath, ContextDir, TodosFile);
        await AtomicFileWriter.WriteAsync(path, content);
    }

    public async Task SavePlanAsync(string worktreePath, string planName, string content)
    {
        Guard.NotNullOrWhiteSpace(worktreePath, nameof(worktreePath));
        Guard.NotNullOrWhiteSpace(planName, nameof(planName));
        Guard.NotNull(content, nameof(content));

        await EnsureContextDirectoryAsync(worktreePath);
        var plansPath = Path.Combine(worktreePath, ContextDir, PlansDir);
        Directory.CreateDirectory(plansPath);

        var fileName = planName.EndsWith(".md") ? planName : $"{planName}.md";
        var path = Path.Combine(plansPath, fileName);
        await AtomicFileWriter.WriteAsync(path, content);
    }

    public Task DeletePlanAsync(string worktreePath, string planName)
    {
        var fileName = planName.EndsWith(".md") ? planName : $"{planName}.md";
        var path = Path.Combine(worktreePath, ContextDir, PlansDir, fileName);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<List<PlanFile>> GetPlansAsync(string worktreePath)
    {
        var plans = new List<PlanFile>();
        var plansPath = Path.Combine(worktreePath, ContextDir, PlansDir);

        if (!Directory.Exists(plansPath))
            return plans;

        foreach (var file in Directory.GetFiles(plansPath, "*.md"))
        {
            plans.Add(new PlanFile
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Content = await File.ReadAllTextAsync(file),
                LastModified = File.GetLastWriteTimeUtc(file)
            });
        }

        return plans.OrderByDescending(p => p.LastModified).ToList();
    }

    public async Task EnsureContextDirectoryAsync(string worktreePath)
    {
        var contextPath = Path.Combine(worktreePath, ContextDir);
        Directory.CreateDirectory(contextPath);
        Directory.CreateDirectory(Path.Combine(contextPath, PlansDir));
        Directory.CreateDirectory(Path.Combine(contextPath, AttachmentsDir));

        // Create empty files if they don't exist
        var notesPath = Path.Combine(contextPath, NotesFile);
        if (!File.Exists(notesPath))
            await AtomicFileWriter.WriteAsync(notesPath, "");

        var todosPath = Path.Combine(contextPath, TodosFile);
        if (!File.Exists(todosPath))
            await AtomicFileWriter.WriteAsync(todosPath, "");

        // Add .context to .gitignore if not already there
        var gitignorePath = Path.Combine(worktreePath, ".gitignore");
        if (File.Exists(gitignorePath))
        {
            var lines = await File.ReadAllLinesAsync(gitignorePath);
            if (!lines.Any(line => line.Trim() == ".context/"))
            {
                await AtomicFileWriter.AppendAsync(gitignorePath, "\n.context/\n");
            }
        }
    }

    public async Task ArchiveContextAsync(string worktreePath, string archivePath)
    {
        var sourceContext = Path.Combine(worktreePath, ContextDir);
        if (!Directory.Exists(sourceContext))
            return;

        var destContext = Path.Combine(archivePath, ContextDir);
        Directory.CreateDirectory(destContext);

        await CopyDirectoryAsync(sourceContext, destContext);
    }

    public string BuildContextPrompt(ContextInfo context)
    {
        Guard.NotNull(context, nameof(context));

        var sb = new StringBuilder();
        var maxItemTokens = CominomiConstants.MaxContextItemTokens;
        var maxTotalTokens = CominomiConstants.MaxContextPromptTokens;

        if (!string.IsNullOrWhiteSpace(context.Notes))
        {
            sb.AppendLine("## Workspace Notes");
            sb.AppendLine(TokenEstimator.Truncate(context.Notes, maxItemTokens));
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(context.Todos))
        {
            sb.AppendLine("## Workspace Todos");
            sb.AppendLine(TokenEstimator.Truncate(context.Todos, maxItemTokens));
            sb.AppendLine();
        }

        foreach (var plan in context.Plans)
        {
            if (TokenEstimator.Estimate(sb.ToString()) >= maxTotalTokens) break;
            sb.AppendLine($"## Plan: {plan.Name}");
            sb.AppendLine(TokenEstimator.Truncate(plan.Content, maxItemTokens));
            sb.AppendLine();
        }

        return TokenEstimator.Truncate(sb.ToString(), maxTotalTokens);
    }

    private static async Task CopyDirectoryAsync(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(dest, Path.GetFileName(file));
            await Task.Run(() => File.Copy(file, destFile, true));
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var destDir = Path.Combine(dest, Path.GetFileName(dir));
            await CopyDirectoryAsync(dir, destDir);
        }
    }
}
