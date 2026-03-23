namespace Cominomi.Shared;

/// <summary>
///     Lightweight input-validation helpers for boundary methods.
///     Throws <see cref="ArgumentNullException" /> or <see cref="ArgumentException" />
///     on invalid input so callers get a clear, immediate failure.
/// </summary>
public static class Guard
{
    public static long NonNegative(long value, string paramName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be non-negative.");
        return value;
    }

    public static string NotNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        return value;
    }

    public static T NotNull<T>(T? value, string paramName) where T : class
    {
        return value ?? throw new ArgumentNullException(paramName);
    }
}