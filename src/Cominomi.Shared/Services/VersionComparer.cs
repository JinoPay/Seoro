using System.Text.RegularExpressions;

namespace Cominomi.Shared.Services;

/// <summary>
///     Semantic version comparison utility for Claude CLI version checking.
/// </summary>
public static partial class VersionComparer
{
    /// <summary>
    ///     Returns true if <paramref name="installed" /> is older than <paramref name="required" />.
    ///     Returns false if either version cannot be parsed (avoids false-positive update nags).
    /// </summary>
    public static bool IsOutdated(string? installed, string required)
    {
        if (string.IsNullOrWhiteSpace(installed))
            return true;

        var installedClean = ExtractVersion(installed);
        var requiredClean = ExtractVersion(required);

        if (!TryParseParts(installedClean, out var iParts) ||
            !TryParseParts(requiredClean, out var rParts))
            return false;

        for (var i = 0; i < Math.Max(iParts.Length, rParts.Length); i++)
        {
            var iv = i < iParts.Length ? iParts[i] : 0;
            var rv = i < rParts.Length ? rParts[i] : 0;
            if (iv < rv) return true;
            if (iv > rv) return false;
        }

        return false; // equal
    }

    private static bool TryParseParts(string version, out int[] parts)
    {
        var segments = version.Split('.');
        parts = new int[segments.Length];
        for (var i = 0; i < segments.Length; i++)
            if (!int.TryParse(segments[i], out parts[i]))
            {
                parts = [];
                return false;
            }

        return segments.Length >= 2;
    }

    [GeneratedRegex(@"\d+\.\d+[\.\d]*")]
    private static partial Regex VersionPattern();

    private static string ExtractVersion(string raw)
    {
        var match = VersionPattern().Match(raw);
        return match.Success ? match.Value : raw.Trim();
    }
}