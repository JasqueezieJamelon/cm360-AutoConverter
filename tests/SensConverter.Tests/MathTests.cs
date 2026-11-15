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

        h.Section("Rounding to two decimals");

        h.Test("a sens is cut to two decimals", () =>
        {
            Harness.AreClose(0.51, SensMath.RoundSens(0.5123), "rounds down");
            Harness.AreClose(0.52, SensMath.RoundSens(0.5167), "rounds up");
            Harness.AreClose(3.14, SensMath.RoundSens(3.14159), "pi");
            Harness.AreClose(12.35, SensMath.RoundSens(12.345), "halfway goes up");
            Harness.AreClose(7, SensMath.RoundSens(7.0), "a whole number stays whole");
        });

        h.Test("a sens that is already short is left alone", () =>
        {
            Harness.AreClose(0.5, SensMath.RoundSens(0.5), "one decimal");
            Harness.AreClose(2.25, SensMath.RoundSens(2.25), "exactly two");
        });

        h.Test("a game needing a tiny sens is not rounded away to zero", () =>
        {
            // Rounding 0.004 to two decimals gives 0.00, and a game set to zero does not turn at
            // all - so these keep two significant figures instead.
            Harness.IsTrue(SensMath.NeedsMoreThanTwoDecimals(0.004123), "0.004 needs more decimals");
            Harness.AreClose(0.0041, SensMath.RoundSens(0.004123), "two significant figures");
            Harness.IsTrue(SensMath.RoundSens(0.0000812) > 0, "never zero");

            Harness.IsFalse(SensMath.NeedsMoreThanTwoDecimals(0.01), "0.01 is fine as it is");
            Harness.IsFalse(SensMath.NeedsMoreThanTwoDecimals(0.5), "so is a normal sens");
        });

        h.Test("rubbish in gives zero rather than a wrong number", () =>
        {
            Harness.AreClose(0, SensMath.RoundSens(0), "zero");
            Harness.AreClose(0, SensMath.RoundSens(-1), "negative");
            Harness.AreClose(0, SensMath.RoundSens(double.NaN), "NaN");
            Harness.AreClose(0, SensMath.RoundSens(double.PositiveInfinity), "infinity");
        });

        h.Test("the cost of rounding is reported honestly", () =>
        {
            // A game whose ideal sens lands exactly on two decimals loses nothing to rounding.
            double exactCounts = 0.5 * SensMath.CountsPer360FromCm(30, 1600);
            double clean = SensMath.CoefficientFromCalibration(exactCounts, 1.0);
            Harness.IsTrue(SensMath.RoundingErrorFraction(clean, 30, 1600) < 1e-9, "an exact answer loses nothing");

            // The README's worked example is very nearly such a game, but not quite: 9449 counts
            // asks for 0.500009, so rounding to 0.50 costs about two thousandths of a percent.
            double worked = SensMath.CoefficientFromCalibration(9449, 1.0);
            Harness.IsTrue(SensMath.RoundingErrorFraction(worked, 30, 1600) < 1e-4, "and a near-exact one almost nothing");

            // A game whose ideal sens is 0.834 has to settle for 0.83, which is a real difference
            // and the program should be able to say how big it is.
            double awkward = SensMath.CoefficientFromCalibration(11331, 1.0);
            double error = SensMath.RoundingErrorFraction(awkward, 30, 1600);
            Harness.IsTrue(error > 0, "an awkward answer does lose something");
            Harness.IsTrue(error < 0.01, $"but under 1 percent, got {error:P2}");
        });

        h.Test("the rounded sens really does produce the distance claimed for it", () =>
        {
            double c = SensMath.CoefficientFromCalibration(11331, 1.0);
            double rounded = SensMath.RoundSens(SensMath.SensForCm360(c, 30, 1600));
            double actual = SensMath.Cm360ForSens(c, rounded, 1600);

            Harness.AreClose(
                SensMath.RoundingErrorFraction(c, 30, 1600),
                Math.Abs(actual - 30) / 30,
                "the reported error matches the real one");
        });

        h.Section("SensMath guards");

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
