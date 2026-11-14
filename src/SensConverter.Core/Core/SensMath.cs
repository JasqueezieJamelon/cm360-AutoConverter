namespace SensConverter.Core;

/// <summary>
/// The whole conversion, in one place.
///
/// Every game that scales mouse input linearly does this internally:
///
///     degrees turned = mouse_counts * in_game_sens * C
///
/// where C is a constant baked into that game ("degrees per count per unit of sens").
/// Once C is known for a game, the sens needed for any desired cm/360 falls out:
///
///     counts per 360 = 360 / (sens * C)
///     cm/360         = counts per 360 / dpi * 2.54
///
/// Nothing here depends on the user's DPI or target - only on C - so changing either
/// never requires recalibrating a game.
/// </summary>
public static class SensMath
{
    public const double CmPerInch = 2.54;

    /// <summary>How many mouse counts a full 360 takes, for a given sweep distance and DPI.</summary>
    public static double CountsPer360FromCm(double cm360, double dpi)
    {
        Require(cm360, nameof(cm360));
        Require(dpi, nameof(dpi));
        return cm360 / CmPerInch * dpi;
    }

    /// <summary>How far the mouse travels for a full 360, in cm, for a given count total and DPI.</summary>
    public static double Cm360FromCounts(double counts360, double dpi)
    {
        Require(counts360, nameof(counts360));
        Require(dpi, nameof(dpi));
        return counts360 / dpi * CmPerInch;
    }

    /// <summary>
    /// Solves for the game's constant C from one calibration: the user spun exactly 360 degrees
    /// using <paramref name="counts360"/> mouse counts while the in-game sens was
    /// <paramref name="sensUsed"/>.
    /// </summary>
    public static double CoefficientFromCalibration(double counts360, double sensUsed)
    {
        Require(counts360, nameof(counts360));
        Require(sensUsed, nameof(sensUsed));
        return 360.0 / (counts360 * sensUsed);
    }

    /// <summary>The in-game sens that makes a 360 take exactly <paramref name="targetCounts360"/> counts.</summary>
    public static double SensForCounts(double coefficient, double targetCounts360)
    {
        Require(coefficient, nameof(coefficient));
        Require(targetCounts360, nameof(targetCounts360));
        return 360.0 / (coefficient * targetCounts360);
    }

    /// <summary>The in-game sens that gives the wanted cm/360 at the user's DPI.</summary>
    public static double SensForCm360(double coefficient, double targetCm360, double dpi)
        => SensForCounts(coefficient, CountsPer360FromCm(targetCm360, dpi));

    /// <summary>The cm/360 that a given in-game sens actually produces. Used to show rounding error.</summary>
    public static double Cm360ForSens(double coefficient, double sens, double dpi)
    {
        Require(coefficient, nameof(coefficient));
        Require(sens, nameof(sens));
        Require(dpi, nameof(dpi));
        return Cm360FromCounts(360.0 / (coefficient * sens), dpi);
    }

    public static double CmToInches(double cm) => cm / CmPerInch;

    public static double InchesToCm(double inches) => inches * CmPerInch;

    private static void Require(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be a positive, finite number.");
        }
    }
}
