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
///
/// Horizontal and vertical are the same maths with their own C. A game with one sens slider has
/// one C; a game with separate X and Y sliders has two, and they are solved independently.
/// </summary>
public static class SensMath
{
    public const double CmPerInch = 2.54;

    /// <summary>Game menus take two decimals, so that is what gets recommended.</summary>
    public const int SensDecimals = 2;

    /// <summary>The smallest sens two decimals can express.</summary>
    public const double SmallestTwoDecimalSens = 0.01;

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

    /// <summary>
    /// Solves for C from a partial turn, which is the only way to measure vertically - you cannot
    /// pitch through a full circle. <paramref name="degrees"/> is how far the view actually moved.
    /// </summary>
    public static double CoefficientFromPartialTurn(double degrees, double counts, double sensUsed)
    {
        Require(degrees, nameof(degrees));
        Require(counts, nameof(counts));
        Require(sensUsed, nameof(sensUsed));
        return degrees / (counts * sensUsed);
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

    /// <summary>
    /// The number to actually type into the game: two decimals, because that is what game menus
    /// accept and what a person can retype without mistakes.
    ///
    /// One exception. A few games scale so hard that the right answer is below 0.005, where two
    /// decimals would round to a flat zero and the game would not turn at all. Those keep two
    /// significant figures instead (0.0041 rather than 0.00), and
    /// <see cref="NeedsMoreThanTwoDecimals"/> reports it so the user can be told why their number
    /// looks different.
    /// </summary>
    public static double RoundSens(double sens)
    {
        if (double.IsNaN(sens) || double.IsInfinity(sens) || sens <= 0)
        {
            return 0;
        }

        if (!NeedsMoreThanTwoDecimals(sens))
        {
            return Math.Round(sens, SensDecimals, MidpointRounding.AwayFromZero);
        }

        // Two significant figures: 0.004123 -> 0.0041, 0.00008 -> 0.00008.
        int leadingZeros = (int)Math.Floor(-Math.Log10(sens));
        int decimals = Math.Min(leadingZeros + 2, 15);
        return Math.Round(sens, decimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>True when two decimals would round this sens away to zero.</summary>
    public static bool NeedsMoreThanTwoDecimals(double sens)
        => sens > 0
            && !double.IsNaN(sens)
            && !double.IsInfinity(sens)
            && Math.Round(sens, SensDecimals, MidpointRounding.AwayFromZero) <= 0;

    /// <summary>
    /// How far off the target the rounded number lands, as a fraction (0.01 = 1% out). Rounding to
    /// two decimals costs accuracy, and on a very low-sens game it can cost a lot, so the UI shows
    /// this rather than pretending the rounded number is exact.
    /// </summary>
    public static double RoundingErrorFraction(double coefficient, double targetCm360, double dpi)
    {
        double exact = SensForCm360(coefficient, targetCm360, dpi);
        double rounded = RoundSens(exact);
        if (rounded <= 0)
        {
            return 0;
        }

        double actualCm360 = Cm360ForSens(coefficient, rounded, dpi);
        return Math.Abs(actualCm360 - targetCm360) / targetCm360;
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
