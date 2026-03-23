namespace Cominomi.Shared;

/// <summary>
///     Estimates token counts from text using character-class heuristics.
///     ASCII/Latin text averages ~4 chars per token; CJK/Korean characters
///     are typically 1–2 tokens each (~1.5 chars per token on average).
/// </summary>
public static class TokenEstimator
{
    private const double AsciiCharsPerToken = 4.0;
    private const double NonAsciiCharsPerToken = 1.5;

    /// <summary>
    ///     Estimates the number of tokens in the given text.
    /// </summary>
    public static int Estimate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var asciiCount = 0;
        var nonAsciiCount = 0;

        foreach (var ch in text)
            if (ch <= 0x7F)
                asciiCount++;
            else
                nonAsciiCount++;

        return (int)Math.Ceiling(asciiCount / AsciiCharsPerToken + nonAsciiCount / NonAsciiCharsPerToken);
    }

    /// <summary>
    ///     Truncates text to approximately the given token budget.
    ///     Returns the original text if it fits within the budget.
    /// </summary>
    public static string Truncate(string text, int maxTokens)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        if (Estimate(text) <= maxTokens) return text;

        // Binary search for the right character count
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            var mid = lo + (hi - lo + 1) / 2;
            if (Estimate(text[..mid]) <= maxTokens)
                lo = mid;
            else
                hi = mid - 1;
        }

        // Avoid splitting in the middle of a surrogate pair
        if (lo > 0 && char.IsHighSurrogate(text[lo - 1]))
            lo--;

        return text[..lo] + string.Format(CominomiConstants.TruncationMarker, Estimate(text));
    }
}