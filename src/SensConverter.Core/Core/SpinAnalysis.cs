namespace SensConverter.Core;

/// <summary>One recorded 360 attempt.</summary>
public sealed class Spin
{
    public Spin(double counts)
    {
        Counts = counts;
    }

    /// <summary>Horizontal mouse counts consumed by the spin (always stored positive).</summary>
    public double Counts { get; }

    /// <summary>Spins can be dropped by the user without being deleted from the record.</summary>
    public bool Kept { get; set; } = true;
}

/// <summary>
/// Judges a set of spins. A good set is tightly clustered; a scattered set usually means either
/// a sloppy spin or that the game applies mouse acceleration, which breaks the whole model.
/// </summary>
public sealed class SpinAnalysis
{
    /// <summary>A spin more than this far from the median is flagged for the user to drop.</summary>
    public const double OutlierTolerance = 0.05;

    /// <summary>If the kept spins still spread this wide, the game is probably not linear.</summary>
    public const double SpreadWarnThreshold = 0.10;

    /// <summary>Fewer kept spins than this and the average is not worth much.</summary>
    public const int RecommendedSpins = 3;

    private SpinAnalysis(int keptCount, double mean, double median, double spreadRatio)
    {
        KeptCount = keptCount;
        Mean = mean;
        Median = median;
        SpreadRatio = spreadRatio;
    }

    public int KeptCount { get; }

    /// <summary>Average of the kept spins. This is the number calibration is built from.</summary>
    public double Mean { get; }

    /// <summary>Median of the kept spins. Outliers are measured against this, not the mean,
    /// so that one bad spin cannot drag the yardstick along with it.</summary>
    public double Median { get; }

    /// <summary>(max - min) / mean across the kept spins.</summary>
    public double SpreadRatio { get; }

    public bool HasAnySpins => KeptCount > 0;

    public bool HasEnoughSpins => KeptCount >= RecommendedSpins;

    public bool HasHighSpread => KeptCount >= 2 && SpreadRatio > SpreadWarnThreshold;

    public static SpinAnalysis Empty { get; } = new SpinAnalysis(0, 0, 0, 0);

    public static SpinAnalysis Analyze(IEnumerable<Spin> spins)
    {
        ArgumentNullException.ThrowIfNull(spins);
        return AnalyzeCounts(spins.Where(s => s.Kept).Select(s => s.Counts));
    }

    public static SpinAnalysis AnalyzeCounts(IEnumerable<double> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        double[] kept = counts.Where(c => c > 0 && !double.IsNaN(c) && !double.IsInfinity(c)).ToArray();
        if (kept.Length == 0)
        {
            return Empty;
        }

        double mean = kept.Average();
        double spread = mean > 0 ? (kept.Max() - kept.Min()) / mean : 0;
        return new SpinAnalysis(kept.Length, mean, MedianOf(kept), spread);
    }

    /// <summary>How far one spin sits from the median, as a fraction (0.04 = 4% off).</summary>
    public double DeviationFromMedian(double counts)
    {
        if (Median <= 0)
        {
            return 0;
        }

        return Math.Abs(counts - Median) / Median;
    }

    public bool IsOutlier(double counts) => KeptCount >= 3 && DeviationFromMedian(counts) > OutlierTolerance;

    public static double MedianOf(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        double[] sorted = values.ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
