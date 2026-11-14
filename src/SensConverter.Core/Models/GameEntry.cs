namespace SensConverter.Core;

/// <summary>One calibrated game.</summary>
public sealed class GameEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public string Name { get; set; } = string.Empty;

    public string ExePath { get; set; } = string.Empty;

    /// <summary>SHA-256 of the exe, so a moved or reinstalled game can be recognised.</summary>
    public string ExeHash { get; set; } = string.Empty;

    /// <summary>The in-game sens that was set while calibrating.</summary>
    public double SensUsedDuringCalibration { get; set; } = 1.0;

    /// <summary>Average mouse counts for a 360 at that sens.</summary>
    public double Counts360 { get; set; }

    /// <summary>The game's constant: degrees per count per unit of sens.</summary>
    public double Coefficient { get; set; }

    /// <summary>Every spin recorded during calibration, kept for the record.</summary>
    public List<double> Spins { get; set; } = new();

    public DateTimeOffset CalibratedOn { get; set; } = DateTimeOffset.Now;

    public string Notes { get; set; } = string.Empty;

    public bool IsCalibrated => Coefficient > 0 && !double.IsNaN(Coefficient) && !double.IsInfinity(Coefficient);

    /// <summary>The sens to set in this game to hit the wanted feel.</summary>
    public double SensFor(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return SensMath.SensForCm360(Coefficient, settings.TargetCm360, settings.Dpi);
    }

    /// <summary>Builds an entry from a finished calibration.</summary>
    public static GameEntry FromCalibration(
        string name,
        string exePath,
        string exeHash,
        double sensUsed,
        IEnumerable<double> keptSpinCounts,
        string notes = "")
    {
        ArgumentNullException.ThrowIfNull(keptSpinCounts);

        double[] spins = keptSpinCounts.ToArray();
        SpinAnalysis analysis = SpinAnalysis.AnalyzeCounts(spins);
        if (!analysis.HasAnySpins)
        {
            throw new ArgumentException("At least one usable spin is required.", nameof(keptSpinCounts));
        }

        return new GameEntry
        {
            Name = name,
            ExePath = exePath,
            ExeHash = exeHash,
            SensUsedDuringCalibration = sensUsed,
            Counts360 = analysis.Mean,
            Coefficient = SensMath.CoefficientFromCalibration(analysis.Mean, sensUsed),
            Spins = spins.ToList(),
            CalibratedOn = DateTimeOffset.Now,
            Notes = notes,
        };
    }

    /// <summary>Recomputes anything derivable, in case the json was hand-edited.</summary>
    public void Normalize()
    {
        Spins ??= new List<double>();

        if (SensUsedDuringCalibration <= 0 || double.IsNaN(SensUsedDuringCalibration))
        {
            SensUsedDuringCalibration = 1.0;
        }

        if (Counts360 <= 0 && Spins.Count > 0)
        {
            Counts360 = SpinAnalysis.AnalyzeCounts(Spins).Mean;
        }

        if (!IsCalibrated && Counts360 > 0)
        {
            Coefficient = SensMath.CoefficientFromCalibration(Counts360, SensUsedDuringCalibration);
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = string.IsNullOrWhiteSpace(ExePath) ? "Unnamed game" : Path.GetFileNameWithoutExtension(ExePath);
        }

        if (string.IsNullOrEmpty(Id))
        {
            Id = Guid.NewGuid().ToString("n");
        }
    }
}
