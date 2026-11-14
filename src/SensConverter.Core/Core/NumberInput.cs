using System.Globalization;

namespace SensConverter.Core;

/// <summary>
/// Typed-in numbers, read forgivingly: "2.5" and "2,5" both work whatever the Windows region is,
/// and stray spaces are ignored.
/// </summary>
public static class NumberInput
{
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        const NumberStyles styles = NumberStyles.Float;

        if (double.TryParse(trimmed, styles, CultureInfo.CurrentCulture, out value)
            || double.TryParse(trimmed, styles, CultureInfo.InvariantCulture, out value))
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        // Last chance: the other separator was used, e.g. "2,5" on a dot-decimal machine.
        string swapped = trimmed.Replace(',', '.');
        if (double.TryParse(swapped, styles, CultureInfo.InvariantCulture, out value))
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        value = 0;
        return false;
    }

    /// <summary>Parses and requires a positive number.</summary>
    public static bool TryParsePositive(string? text, out double value)
        => TryParse(text, out value) && value > 0;

    /// <summary>
    /// Formats a sensitivity for display: enough decimals to be useful, no trailing zero noise.
    /// </summary>
    public static string FormatSens(double sens)
    {
        if (double.IsNaN(sens) || double.IsInfinity(sens))
        {
            return "-";
        }

        double abs = Math.Abs(sens);
        string format = abs switch
        {
            >= 100 => "0.##",
            >= 10 => "0.###",
            >= 1 => "0.####",
            >= 0.1 => "0.#####",
            _ => "0.#######",
        };

        return sens.ToString(format, CultureInfo.CurrentCulture);
    }

    public static string FormatCounts(double counts)
        => double.IsNaN(counts) || double.IsInfinity(counts)
            ? "-"
            : Math.Round(counts).ToString("N0", CultureInfo.CurrentCulture);

    public static string FormatDistance(double value)
        => double.IsNaN(value) || double.IsInfinity(value)
            ? "-"
            : value.ToString("0.##", CultureInfo.CurrentCulture);

    public static string FormatPercent(double fraction)
        => double.IsNaN(fraction) || double.IsInfinity(fraction)
            ? "-"
            : (fraction * 100).ToString("0.0", CultureInfo.CurrentCulture) + "%";
}
