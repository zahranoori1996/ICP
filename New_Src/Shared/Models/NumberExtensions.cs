namespace Shared.Models;

/// <summary>
/// Provides extension methods for number formatting.
/// </summary>
public static class NumberExtensions
{
    /// <summary>
    /// Formats a decimal value with specified decimal places and removes trailing zeros.
    /// </summary>
    /// <param name="value">The decimal value to format.</param>
    /// <param name="decimalPlaces">The number of decimal places to use (default is 2).</param>
    /// <returns>A string representation of the number with trailing zeros removed.</returns>
    public static string FormatPython(this decimal value, int decimalPlaces = 2)
    {
        var formatted = value.ToString($"F{decimalPlaces}");
        formatted = formatted.TrimEnd('0').TrimEnd('.');
        return string.IsNullOrEmpty(formatted) ? "0" : formatted;
    }

    /// <summary>
    /// Formats a nullable decimal value with specified decimal places and removes trailing zeros.
    /// </summary>
    /// <param name="value">The nullable decimal value to format.</param>
    /// <param name="decimalPlaces">The number of decimal places to use (default is 2).</param>
    /// <returns>A string representation of the number, or null if the value is null.</returns>
    public static string? FormatPython(this decimal? value, int decimalPlaces = 2)
    {
        return value?.FormatPython(decimalPlaces);
    }

    /// <summary>
    /// Formats a double value with specified decimal places and removes trailing zeros.
    /// </summary>
    /// <param name="value">The double value to format.</param>
    /// <param name="decimalPlaces">The number of decimal places to use (default is 2).</param>
    /// <returns>A string representation of the number with trailing zeros removed.</returns>
    public static string FormatPython(this double value, int decimalPlaces = 2)
    {
        return ((decimal)value).FormatPython(decimalPlaces);
    }

    /// <summary>
    /// Formats a nullable double value with specified decimal places and removes trailing zeros.
    /// </summary>
    /// <param name="value">The nullable double value to format.</param>
    /// <param name="decimalPlaces">The number of decimal places to use (default is 2).</param>
    /// <returns>A string representation of the number, or null if the value is null.</returns>
    public static string? FormatPython(this double? value, int decimalPlaces = 2)
    {
        return value?.FormatPython(decimalPlaces);
    }
}
