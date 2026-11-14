using SensConverter.Core;

namespace SensConverter.Tests;

internal static class MathTests
{
    public static void Register(Harness h)
    {
        ArgumentNullException.ThrowIfNull(h);
        h.Section("SensMath");

        h.Test("30 cm at 1600 DPI is 18897.6 counts", () =>
            Harness.AreClose(18897.637795275591, SensMath.CountsPer360FromCm(30, 1600), "counts", 1e-9));

        h.Test("counts and cm round-trip", () =>
        {
            double counts = SensMath.CountsPer360FromCm(27.5, 800);
            Harness.AreClose(27.5, SensMath.Cm360FromCounts(counts, 800), "cm back");
        });

        h.Test("the spec worked example lands on sens 0.5", () =>
        {
            // 9449 counts for a 360 at sens 1.0, wanting 30 cm/360 at 1600 DPI.
            double c = SensMath.CoefficientFromCalibration(9449, 1.0);
            double sens = SensMath.SensForCm360(c, 30, 1600);
            Harness.AreClose(0.5, sens, "sens", 1e-4);
        });

        h.Test("coefficient is degrees per count per unit of sens", () =>
        {
            double c = SensMath.CoefficientFromCalibration(9449, 1.0);
            Harness.AreClose(360.0 / 9449.0, c, "coefficient");

            // Same game measured at sens 2 takes half as many counts, so C must come out the same.
            double cAtTwo = SensMath.CoefficientFromCalibration(9449 / 2.0, 2.0);
            Harness.AreClose(c, cAtTwo, "coefficient is independent of the sens used during calibration");
        });

        h.Test("suggested sens reproduces the wanted distance exactly", () =>
        {
            double c = SensMath.CoefficientFromCalibration(12345, 3.7);
            double sens = SensMath.SensForCm360(c, 24.4, 3200);
            Harness.AreClose(24.4, SensMath.Cm360ForSens(c, sens, 3200), "cm/360 achieved");
        });

        h.Test("doubling DPI halves the sens needed", () =>
        {
            double c = SensMath.CoefficientFromCalibration(9449, 1.0);
            double low = SensMath.SensForCm360(c, 30, 800);
            double high = SensMath.SensForCm360(c, 30, 1600);
            Harness.AreClose(low / 2.0, high, "sens at double DPI");
        });

        h.Test("doubling the target distance halves the sens", () =>
        {
            double c = SensMath.CoefficientFromCalibration(9449, 1.0);
            Harness.AreClose(
                SensMath.SensForCm360(c, 20, 1600) / 2.0,
                SensMath.SensForCm360(c, 40, 1600),
                "sens at double distance");
        });

        h.Test("two games with the same C want the same sens", () =>
        {
            double a = SensMath.CoefficientFromCalibration(10000, 2.0);
            double b = SensMath.CoefficientFromCalibration(5000, 4.0);
            Harness.AreClose(SensMath.SensForCm360(a, 30, 1600), SensMath.SensForCm360(b, 30, 1600), "matched sens");
        });

        h.Test("inches and cm convert both ways", () =>
        {
            Harness.AreClose(11.811023622047244, SensMath.CmToInches(30), "cm to in");
            Harness.AreClose(30, SensMath.InchesToCm(SensMath.CmToInches(30)), "round-trip");
        });

        h.Test("nonsense inputs are rejected rather than producing silent garbage", () =>
        {
            Harness.Throws<ArgumentOutOfRangeException>(() => SensMath.CountsPer360FromCm(0, 1600), "zero cm");
            Harness.Throws<ArgumentOutOfRangeException>(() => SensMath.CountsPer360FromCm(30, -1), "negative dpi");
            Harness.Throws<ArgumentOutOfRangeException>(() => SensMath.CoefficientFromCalibration(0, 1), "zero counts");
            Harness.Throws<ArgumentOutOfRangeException>(() => SensMath.CoefficientFromCalibration(100, 0), "zero sens");
            Harness.Throws<ArgumentOutOfRangeException>(() => SensMath.SensForCounts(double.NaN, 100), "NaN coefficient");
            Harness.Throws<ArgumentOutOfRangeException>(() => SensMath.Cm360ForSens(0.03, 1, double.PositiveInfinity), "infinite dpi");
        });
    }
}
