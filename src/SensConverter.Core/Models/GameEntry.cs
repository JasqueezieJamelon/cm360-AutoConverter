namespace SensConverter.Core;

/// <summary>How a game's measurement was taken.</summary>
public enum MeasurementMethod
{
    /// <summary>The user spun by hand and pressed the hotkey at each end.</summary>
    Manual = 0,

    /// <summary>The program turned the view itself and read the screen.</summary>
    Automatic = 1,
}

/// <summary>
/// One calibrated game.
///
/// Horizontal is always measured. Vertical is optional, and only worth measuring on a game that
/// has its own separate vertical sens slider - on a game with one slider there is nothing to set
/// the vertical number to. <see cref="CoefficientY"/> is zero when vertical was never measured.
/// </summary>
public sealed class GameEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public string Name { get; set; } = string.Empty;

    public string ExePath { get; set; } = string.Empty;

    /// <summary>SHA-256 of the exe, so a moved or reinstalled game can be recognised.</summary>
    public string ExeHash { get; set; } = string.Empty;

    /// <summary>The in-game (horizontal) sens that was set while calibrating.</summary>
    public double SensUsedDuringCalibration { get; set; } = 1.0;

    /// <summary>Average mouse counts for a 360 at that sens.</summary>
    public double Counts360 { get; set; }

    /// <summary>The game's horizontal constant: degrees per count per unit of sens.</summary>
    public double Coefficient { get; set; }

    /// <summary>Every horizontal spin recorded during calibration, kept for the record.</summary>
    public List<double> Spins { get; set; } = new();

    /// <summary>
    /// The game's vertical constant, in the same units as <see cref="Coefficient"/>. Zero when
    /// vertical was not measured.
    /// </summary>
    public double CoefficientY { get; set; }

    /// <summary>The in-game vertical sens that was set while measuring vertically.</summary>
    public double SensUsedDuringCalibrationY { get; set; } = 1.0;

    /// <summary>How the measurement was taken, for the record.</summary>
    public MeasurementMethod Method { get; set; } = MeasurementMethod.Manual;

    public DateTimeOffset CalibratedOn { get; set; } = DateTimeOffset.Now;

    public string Notes { get; set; } = string.Empty;

    public bool IsCalibrated => IsUsableCoefficient(Coefficient);

    /// <summary>True when this game has its own vertical measurement.</summary>
    public bool HasVertical => IsUsableCoefficient(CoefficientY);

    /// <summary>
    /// How much more sensitive the game's vertical axis is than its horizontal one, at equal sens
    /// settings. 1.0 means the game treats both axes alike. Zero when vertical was not measured.
    /// </summary>
    public double MeasuredVerticalRatio
        => HasVertical && IsCalibrated ? CoefficientY / Coefficient : 0;

    /// <summary>The exact horizontal sens to set in this game to hit the wanted feel.</summary>
    public double SensFor(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return SensMath.SensForCm360(Coefficient, settings.TargetCm360, settings.Dpi);
    }

    /// <summary>The horizontal sens to actually type in, rounded to two decimals.</summary>
    public double RoundedSensFor(AppSettings settings) => SensMath.RoundSens(SensFor(settings));

    /// <summary>The exact vertical sens, or zero when vertical was not measured.</summary>
    public double SensYFor(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return HasVertical
            ? SensMath.SensForCm360(CoefficientY, settings.TargetCm360Vertical, settings.Dpi)
            : 0;
    }

    /// <summary>The vertical sens to actually type in, rounded to two decimals.</summary>
    public double RoundedSensYFor(AppSettings settings) => SensMath.RoundSens(SensYFor(settings));

    /// <summary>
    /// The cm/360 the rounded horizontal number really produces. Shown so the user can see what
    /// rounding to two decimals cost them.
    /// </summary>
    public double ActualCm360For(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        double rounded = RoundedSensFor(settings);
        return rounded > 0 ? SensMath.Cm360ForSens(Coefficient, rounded, settings.Dpi) : 0;
    }

    /// <summary>The vertical equivalent of <see cref="ActualCm360For"/>.</summary>
    public double ActualCm360VerticalFor(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        double rounded = RoundedSensYFor(settings);
        return rounded > 0 ? SensMath.Cm360ForSens(CoefficientY, rounded, settings.Dpi) : 0;
    }

    /// <summary>Builds an entry from a finished horizontal calibration.</summary>
    public static GameEntry FromCalibration(
        string name,
        string exePath,
        string exeHash,
        double sensUsed,
        IEnumerable<double> keptSpinCounts,
        string notes = "",
        MeasurementMethod method = MeasurementMethod.Manual)
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
            Method = method,
        };
    }

    /// <summary>
    /// Records a vertical measurement. Vertical cannot be measured with a full circle - you cannot
    /// pitch all the way round - so it is given as a part turn: the view moved
    /// <paramref name="degrees"/> for <paramref name="counts"/> mouse counts.
    /// </summary>
    public void SetVertical(double degrees, double counts, double sensUsedY)
    {
        if (degrees <= 0 || counts <= 0 || sensUsedY <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(degrees),
                "A vertical measurement needs a positive turn, count and sens.");
        }

        CoefficientY = SensMath.CoefficientFromPartialTurn(degrees, counts, sensUsedY);
        SensUsedDuringCalibrationY = sensUsedY;
    }

    /// <summary>Forgets the vertical measurement, leaving horizontal alone.</summary>
    public void ClearVertical()
    {
        CoefficientY = 0;
        SensUsedDuringCalibrationY = 1.0;
    }

    /// <summary>Recomputes anything derivable, in case the json was hand-edited.</summary>
    public void Normalize()
    {
        Spins ??= new List<double>();

        if (SensUsedDuringCalibration <= 0 || double.IsNaN(SensUsedDuringCalibration))
        {
            SensUsedDuringCalibration = 1.0;
        }

        if (SensUsedDuringCalibrationY <= 0 || double.IsNaN(SensUsedDuringCalibrationY))
        {
            SensUsedDuringCalibrationY = 1.0;
        }

        if (Counts360 <= 0 && Spins.Count > 0)
        {
            Counts360 = SpinAnalysis.AnalyzeCounts(Spins).Mean;
        }

        if (!IsCalibrated && Counts360 > 0)
        {
            Coefficient = SensMath.CoefficientFromCalibration(Counts360, SensUsedDuringCalibration);
        }

        // A nonsense vertical value counts as "never measured" rather than poisoning the display.
        if (!IsUsableCoefficient(CoefficientY))
        {
            CoefficientY = 0;
        }

        if (!Enum.IsDefined(Method))
        {
            Method = MeasurementMethod.Manual;
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

    private static bool IsUsableCoefficient(double value)
        => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
