namespace SensConverter.Core;

/// <summary>Your profile: the mouse and the feel you want, and nothing game-specific.</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Mouse DPI (counts per inch) as configured on the mouse itself.</summary>
    public double Dpi { get; set; } = 800;

    /// <summary>Wanted sweep distance for a full 360, in centimetres.</summary>
    public double TargetCm360 { get; set; } = 30;

    /// <summary>Global key that starts and stops a spin recording.</summary>
    public string CalibrationHotkey { get; set; } = "F8";

    /// <summary>How many spins the calibration wizard asks for.</summary>
    public int SpinsPerCalibration { get; set; } = 5;

    /// <summary>False until the first-run setup screen has been completed once.</summary>
    public bool Configured { get; set; }

    public AppSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Dpi = Dpi,
        TargetCm360 = TargetCm360,
        CalibrationHotkey = CalibrationHotkey,
        SpinsPerCalibration = SpinsPerCalibration,
        Configured = Configured,
    };

    /// <summary>Clamps anything nonsensical that was hand-edited into the json.</summary>
    public void Normalize()
    {
        if (!IsUsable(Dpi))
        {
            Dpi = 800;
        }

        if (!IsUsable(TargetCm360))
        {
            TargetCm360 = 30;
        }

        Dpi = Math.Clamp(Dpi, 1, 100_000);
        TargetCm360 = Math.Clamp(TargetCm360, 0.1, 10_000);
        SpinsPerCalibration = Math.Clamp(SpinsPerCalibration, 1, 20);

        if (string.IsNullOrWhiteSpace(CalibrationHotkey) || !Hotkey.TryParse(CalibrationHotkey, out _, out _))
        {
            CalibrationHotkey = "F8";
        }
    }

    private static bool IsUsable(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
}
