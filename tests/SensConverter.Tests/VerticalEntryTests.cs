using SensConverter.Core;

namespace SensConverter.Tests;

/// <summary>
/// The X and Y side of a game entry: measuring a vertical axis, the sens it leads to, and what
/// happens to games that were saved before vertical existed.
/// </summary>
internal static class VerticalEntryTests
{
    /// <summary>The worked example from the README: 9449 counts for a 360 at sens 1.0.</summary>
    private static GameEntry Entry()
        => GameEntry.FromCalibration("Test", @"C:\g\t.exe", "hash", 1.0, new[] { 9449.0 });

    private static AppSettings Settings(double verticalMultiplier = 1.0)
        => new() { Dpi = 1600, TargetCm360 = 30, VerticalMultiplier = verticalMultiplier, Configured = true };

    /// <summary>Degrees the view would turn for a given number of counts on that example game.</summary>
    private static double DegreesForCounts(double counts, double ratio = 1.0)
        => 360.0 / 9449.0 * counts * ratio;

    public static void Register(Harness h)
    {
        ArgumentNullException.ThrowIfNull(h);
        h.Section("Vertical sensitivity");

        h.Test("a fresh entry has no vertical measurement", () =>
        {
            GameEntry entry = Entry();
            Harness.IsFalse(entry.HasVertical, "nothing measured yet");
            Harness.AreClose(0, entry.SensYFor(Settings()), "no vertical sens to give");
            Harness.AreClose(0, entry.MeasuredVerticalRatio, "no ratio either");
        });

        h.Test("a game that treats both axes alike wants the same number twice", () =>
        {
            GameEntry entry = Entry();
            entry.SetVertical(DegreesForCounts(1000), 1000, 1.0);

            Harness.IsTrue(entry.HasVertical, "measured");
            Harness.AreClose(1.0, entry.MeasuredVerticalRatio, "ratio of one", 1e-6);
            Harness.AreClose(entry.SensFor(Settings()), entry.SensYFor(Settings()), "same sens both ways", 1e-6);
        });

        h.Test("a game with a slow vertical axis needs a higher vertical number", () =>
        {
            GameEntry entry = Entry();
            entry.SetVertical(DegreesForCounts(1000, 0.5), 1000, 1.0);

            Harness.AreClose(0.5, entry.MeasuredVerticalRatio, "half as sensitive", 1e-6);
            Harness.AreClose(entry.SensFor(Settings()) * 2, entry.SensYFor(Settings()), "double the sens", 1e-6);
        });

        h.Test("the vertical sens accounts for the slider it was measured at", () =>
        {
            // Same game, but the Y slider happened to be on 2.0 while it was measured. The answer
            // has to come out the same, because the scaling inside the game has not changed.
            GameEntry atOne = Entry();
            atOne.SetVertical(DegreesForCounts(1000), 1000, 1.0);

            GameEntry atTwo = Entry();
            atTwo.SetVertical(DegreesForCounts(1000) * 2, 1000, 2.0);

            Harness.AreClose(atOne.CoefficientY, atTwo.CoefficientY, "same vertical constant", 1e-9);
            Harness.AreClose(atOne.SensYFor(Settings()), atTwo.SensYFor(Settings()), "same recommendation", 1e-9);
        });

        h.Test("wanting a slower vertical axis lowers the vertical number", () =>
        {
            GameEntry entry = Entry();
            entry.SetVertical(DegreesForCounts(1000), 1000, 1.0);

            double even = entry.SensYFor(Settings(1.0));
            double half = entry.SensYFor(Settings(0.5));

            Harness.AreClose(even / 2, half, "half as sensitive vertically", 1e-6);
            Harness.AreClose(60, Settings(0.5).TargetCm360Vertical, "which is twice the sweep distance");
        });

        h.Test("both axes are rounded to two decimals", () =>
        {
            GameEntry entry = Entry();
            entry.SetVertical(DegreesForCounts(1000, 0.77), 1000, 1.0);

            Harness.AreClose(0.5, entry.RoundedSensFor(Settings()), "horizontal", 1e-9);
            Harness.AreClose(0.65, entry.RoundedSensYFor(Settings()), "vertical", 1e-9);
        });

        h.Test("the distance the rounded numbers really give is reported", () =>
        {
            GameEntry entry = Entry();
            entry.SetVertical(DegreesForCounts(1000, 0.77), 1000, 1.0);
            AppSettings settings = Settings();

            double horizontal = entry.ActualCm360For(settings);
            Harness.IsTrue(
                Math.Abs(horizontal - 30) / 30 < 1e-4,
                $"horizontal lands on the target, got {horizontal:F4} cm");

            double vertical = entry.ActualCm360VerticalFor(settings);
            Harness.IsTrue(Math.Abs(vertical - 30) / 30 < 0.01, $"vertical within 1 percent, got {vertical:F2} cm");
        });

        h.Test("clearing the vertical measurement leaves horizontal alone", () =>
        {
            GameEntry entry = Entry();
            double horizontal = entry.SensFor(Settings());
            entry.SetVertical(DegreesForCounts(1000), 1000, 1.0);
            entry.ClearVertical();

            Harness.IsFalse(entry.HasVertical, "forgotten");
            Harness.IsTrue(entry.IsCalibrated, "still calibrated horizontally");
            Harness.AreClose(horizontal, entry.SensFor(Settings()), "horizontal untouched");
        });

        h.Test("a nonsense vertical measurement is refused", () =>
        {
            GameEntry entry = Entry();
            Harness.Throws<ArgumentOutOfRangeException>(() => entry.SetVertical(0, 1000, 1.0), "no turn");
            Harness.Throws<ArgumentOutOfRangeException>(() => entry.SetVertical(10, 0, 1.0), "no counts");
            Harness.Throws<ArgumentOutOfRangeException>(() => entry.SetVertical(10, 1000, 0), "no sens");
        });

        h.Test("a hand-edited vertical value that cannot be true is discarded on load", () =>
        {
            GameEntry entry = Entry();
            entry.CoefficientY = -5;
            entry.Normalize();
            Harness.IsFalse(entry.HasVertical, "negative discarded");

            entry.CoefficientY = double.NaN;
            entry.Normalize();
            Harness.IsFalse(entry.HasVertical, "NaN discarded");

            entry.SensUsedDuringCalibrationY = 0;
            entry.Normalize();
            Harness.AreClose(1.0, entry.SensUsedDuringCalibrationY, "zero sens repaired");
        });

        h.Test("a game saved before vertical existed still works", () =>
        {
            // An entry from the old schema has no vertical fields at all. It has to load, convert
            // and display as a horizontal-only game rather than failing or showing a zero.
            GameEntry old = new()
            {
                Name = "Old Game",
                SensUsedDuringCalibration = 1.0,
                Spins = new List<double> { 9449 },
            };
            old.Normalize();

            Harness.IsTrue(old.IsCalibrated, "horizontal recovered from the spins");
            Harness.IsFalse(old.HasVertical, "and no vertical invented for it");
            Harness.AreClose(0.5, old.RoundedSensFor(Settings()), "still gives the right number", 1e-9);
        });

        h.Test("how a measurement was taken is remembered", () =>
        {
            GameEntry manual = Entry();
            Harness.AreEqual(MeasurementMethod.Manual, manual.Method, "hand-measured by default");

            GameEntry automatic = GameEntry.FromCalibration(
                "Auto", string.Empty, string.Empty, 1.0, new[] { 9449.0 }, string.Empty, MeasurementMethod.Automatic);
            Harness.AreEqual(MeasurementMethod.Automatic, automatic.Method, "recorded as automatic");

            automatic.Method = (MeasurementMethod)99;
            automatic.Normalize();
            Harness.AreEqual(MeasurementMethod.Manual, automatic.Method, "a bogus value falls back");
        });

        h.Test("the vertical target follows the multiplier, and nonsense is clamped", () =>
        {
            AppSettings settings = new() { TargetCm360 = 30, VerticalMultiplier = 1.0 };
            Harness.AreClose(30, settings.TargetCm360Vertical, "even by default");

            settings.VerticalMultiplier = 0;
            settings.Normalize();
            Harness.AreClose(1.0, settings.VerticalMultiplier, "zero repaired");

            settings.VerticalMultiplier = 1000;
            settings.Normalize();
            Harness.AreClose(100, settings.VerticalMultiplier, "absurd value clamped");
        });

        h.Test("settings carry the vertical multiplier and the auto-measure consent through a clone", () =>
        {
            AppSettings settings = new() { VerticalMultiplier = 0.75, AutoMeasureAccepted = true };
            AppSettings copy = settings.Clone();

            Harness.AreClose(0.75, copy.VerticalMultiplier, "multiplier copied");
            Harness.IsTrue(copy.AutoMeasureAccepted, "consent copied");
        });
    }
}
