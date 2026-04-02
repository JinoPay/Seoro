namespace Cominomi.Shared.Models;

public enum ConventionalCommitType
{
    Feat,
    Fix,
    Refactor,
    Docs,
    Test,
    Chore,
    Style,
    Perf,
    Ci,
    Build
}

public static class ConventionalCommitTypes
{
    public static readonly List<(ConventionalCommitType Type, string Prefix, string Label)> All =
    [
        (ConventionalCommitType.Feat, "feat", "기능 추가"),
        (ConventionalCommitType.Fix, "fix", "버그 수정"),
        (ConventionalCommitType.Refactor, "refactor", "리팩토링"),
        (ConventionalCommitType.Docs, "docs", "문서"),
        (ConventionalCommitType.Test, "test", "테스트"),
        (ConventionalCommitType.Chore, "chore", "잡무"),
        (ConventionalCommitType.Style, "style", "스타일"),
        (ConventionalCommitType.Perf, "perf", "성능"),
        (ConventionalCommitType.Ci, "ci", "CI/CD"),
        (ConventionalCommitType.Build, "build", "빌드")
    ];

    public static string FormatMessage(ConventionalCommitType type, string? scope, string description)
    {
        var prefix = GetPrefix(type);
        return string.IsNullOrWhiteSpace(scope)
            ? $"{prefix}: {description}"
            : $"{prefix}({scope}): {description}";
    }

    public static string GetLabel(ConventionalCommitType type)
    {
        return All.First(x => x.Type == type).Label;
    }

    public static string GetPrefix(ConventionalCommitType type)
    {
        return All.First(x => x.Type == type).Prefix;
    }
}