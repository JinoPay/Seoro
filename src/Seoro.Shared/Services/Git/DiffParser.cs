using System.Text.RegularExpressions;

namespace Seoro.Shared.Services.Git;

public static partial class DiffParser
{
    public static ParsedDiff Parse(string unifiedDiff)
    {
        var result = new ParsedDiff();
        if (string.IsNullOrWhiteSpace(unifiedDiff))
            return result;

        var lines = unifiedDiff.Split('\n');
        DiffHunk? currentHunk = null;
        var prevHunkNewEnd = 1; // 1-based line after previous hunk's new-side range

        foreach (var line in lines)
        {
            var match = HunkHeaderRegex().Match(line);
            if (match.Success)
            {
                var hunk = new DiffHunk
                {
                    OldStart = int.Parse(match.Groups[1].Value),
                    OldCount = match.Groups[2].Value is { Length: > 0 } oc ? int.Parse(oc) : 1,
                    NewStart = int.Parse(match.Groups[3].Value),
                    NewCount = match.Groups[4].Value is { Length: > 0 } nc ? int.Parse(nc) : 1,
                    HeaderText = line
                };

                hunk.GapStartLine = prevHunkNewEnd;
                hunk.GapEndLine = hunk.NewStart;

                prevHunkNewEnd = hunk.NewStart + hunk.NewCount;
                currentHunk = hunk;
                result.Hunks.Add(hunk);
                continue;
            }

            if (currentHunk == null)
            {
                // Meta lines (diff --git, ---, +++ etc.)
                result.MetaLines.Add(new DiffLine
                {
                    Type = DiffLineType.Meta,
                    Text = line,
                    RawLine = line
                });
                continue;
            }

            var diffLine = new DiffLine { RawLine = line };
            if (line.StartsWith('+') && !line.StartsWith("+++"))
            {
                diffLine.Type = DiffLineType.Addition;
                diffLine.Prefix = "+";
                diffLine.Text = line[1..];
            }
            else if (line.StartsWith('-') && !line.StartsWith("---"))
            {
                diffLine.Type = DiffLineType.Deletion;
                diffLine.Prefix = "-";
                diffLine.Text = line[1..];
            }
            else if (line.StartsWith(' '))
            {
                diffLine.Type = DiffLineType.Context;
                diffLine.Prefix = " ";
                diffLine.Text = line[1..];
            }
            else
            {
                diffLine.Type = DiffLineType.Context;
                diffLine.Text = line;
            }

            currentHunk.Lines.Add(diffLine);
        }

        return result;
    }

    [GeneratedRegex(@"^@@ -(\d+),?(\d*) \+(\d+),?(\d*) @@")]
    private static partial Regex HunkHeaderRegex();
}