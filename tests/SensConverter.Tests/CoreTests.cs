using SensConverter.Core;

namespace SensConverter.Tests;

internal static class CoreTests
{
    public static void Register(Harness h)
    {
        ArgumentNullException.ThrowIfNull(h);

        RegisterSpinAnalysis(h);
        RegisterSpinRecorder(h);
        RegisterHotkey(h);
        RegisterNumberInput(h);
        RegisterExeIdentity(h);
    }

    private static void RegisterSpinAnalysis(Harness h)
    {
        h.Section("SpinAnalysis");

        double[] tight = { 9400, 9450, 9460, 9500 };
        double[] withOutlier = { 9400, 9450, 9460, 9500, 12000 };

        h.Test("mean and median of a tight set", () =>
        {
            SpinAnalysis a = SpinAnalysis.AnalyzeCounts(tight);
            Harness.AreEqual(4, a.KeptCount, "kept");
            Harness.AreClose(9452.5, a.Mean, "mean");
            Harness.AreClose(9455, a.Median, "median");
            Harness.IsFalse(a.HasHighSpread, "tight set is not scattered");
            Harness.IsTrue(a.HasEnoughSpins, "four spins is enough");
        });

        h.Test("the odd spin out is flagged, the good ones are not", () =>
        {
            SpinAnalysis a = SpinAnalysis.AnalyzeCounts(withOutlier);
            Harness.IsTrue(a.IsOutlier(12000), "12000 is an outlier");
            Harness.IsFalse(a.IsOutlier(9400), "9400 is fine");
            Harness.IsFalse(a.IsOutlier(9500), "9500 is fine");
        });

        h.Test("outliers are measured against the median, not the mean", () =>
        {
            // The mean is dragged to 9962 by the outlier; the median stays at 9460.
            SpinAnalysis a = SpinAnalysis.AnalyzeCounts(withOutlier);
            Harness.AreClose(9460, a.Median, "median unmoved");
            Harness.AreClose(9962, a.Mean, "mean dragged", 1e-6);
        });

        h.Test("a scattered set warns about acceleration", () =>
        {
            SpinAnalysis a = SpinAnalysis.AnalyzeCounts(withOutlier);
            Harness.IsTrue(a.HasHighSpread, "26% spread is too much");
            Harness.AreClose((12000.0 - 9400.0) / a.Mean, a.SpreadRatio, "spread ratio");
        });

        h.Test("a single spin is never called an outlier", () =>
        {
            SpinAnalysis a = SpinAnalysis.AnalyzeCounts(new[] { 9449.0 });
            Harness.IsFalse(a.IsOutlier(9449), "nothing to compare against");
            Harness.IsFalse(a.HasEnoughSpins, "one spin is not enough");
            Harness.IsFalse(a.HasHighSpread, "one spin cannot be scattered");
        });

        h.Test("empty and junk inputs give an empty analysis", () =>
        {
            Harness.IsFalse(SpinAnalysis.AnalyzeCounts(Array.Empty<double>()).HasAnySpins, "empty");
            Harness.IsFalse(SpinAnalysis.AnalyzeCounts(new[] { 0.0, -5.0, double.NaN }).HasAnySpins, "junk filtered");
        });

        h.Test("dropped spins are left out of the average", () =>
        {
            List<Spin> spins = new() { new Spin(9400), new Spin(9500), new Spin(12000) { Kept = false } };
            SpinAnalysis a = SpinAnalysis.Analyze(spins);
            Harness.AreEqual(2, a.KeptCount, "kept count");
            Harness.AreClose(9450, a.Mean, "mean of kept");
        });

        h.Test("median of an odd count is the middle value", () =>
        {
            Harness.AreClose(5, SpinAnalysis.MedianOf(new[] { 9.0, 1.0, 5.0 }), "median");
            Harness.AreClose(0, SpinAnalysis.MedianOf(Array.Empty<double>()), "median of nothing");
        });
    }

    private static void RegisterSpinRecorder(Harness h)
    {
        h.Section("SpinRecorder");

        h.Test("movement before Start is ignored", () =>
        {
            SpinRecorder r = new();
            r.Add(500, 0);
            Harness.AreClose(0, r.NetCounts, "nothing counted");
        });

        h.Test("overshoot and correction cancel out", () =>
        {
            SpinRecorder r = new();
            r.Start();
            r.Add(5000, 0);
            r.Add(-200, 10);
            SpinRecord record = r.Stop();

            Harness.AreClose(4800, record.NetCounts, "net");
            Harness.AreClose(5200, record.TravelCounts, "travel");
            Harness.AreClose(4800, record.Counts, "counts used");
            Harness.AreEqual(null, record.Warning, "a small correction is not worth warning about");
            Harness.IsTrue(record.IsUsable, "usable");
            Harness.IsFalse(r.IsRecording, "stopped");
        });

        h.Test("leftward spins count the same as rightward", () =>
        {
            SpinRecorder r = new();
            r.Start();
            r.Add(-9449, 0);
            SpinRecord record = r.Stop();
            Harness.AreClose(9449, record.Counts, "absolute counts");
        });

        h.Test("a tiny movement is not a spin", () =>
        {
            SpinRecorder r = new();
            r.Start();
            r.Add(40, 0);
            SpinRecord record = r.Stop();
            Harness.IsFalse(record.IsUsable, "too small");
            Harness.IsTrue(record.Warning?.Contains("Barely", StringComparison.Ordinal) == true, "warns");
        });

        h.Test("heavy back-and-forth is called out", () =>
        {
            SpinRecorder r = new();
            r.Start();
            r.Add(5000, 0);
            r.Add(-3000, 0);
            r.Add(3000, 0);
            SpinRecord record = r.Stop();
            Harness.AreClose(5000, record.Counts, "net");
            Harness.AreClose(2.2, record.WobbleRatio, "wobble ratio");
            Harness.IsTrue(record.Warning?.Contains("back-and-forth", StringComparison.Ordinal) == true, "warns");
        });

        h.Test("a very tilted sweep is called out", () =>
        {
            SpinRecorder r = new();
            r.Start();
            r.Add(1000, 900);
            SpinRecord record = r.Stop();
            Harness.IsTrue(record.Warning?.Contains("tilted", StringComparison.Ordinal) == true, "warns");
        });

        h.Test("Reset clears everything", () =>
        {
            SpinRecorder r = new();
            r.Start();
            r.Add(1000, 0);
            r.Reset();
            Harness.AreClose(0, r.NetCounts, "net");
            Harness.AreClose(0, r.TravelCounts, "travel");
            Harness.IsFalse(r.IsRecording, "not recording");
        });
    }

    private static void RegisterHotkey(Harness h)
    {
        h.Section("Hotkey");

        h.Test("a bare function key parses", () =>
        {
            Harness.IsTrue(Hotkey.TryParse("F8", out Hotkey? hk, out _), "parsed");
            Harness.AreEqual(0x77u, hk!.VirtualKey, "virtual key");
            Harness.AreEqual(0u, hk.Modifiers, "no modifiers");
            Harness.AreEqual("F8", hk.Display, "display");
        });

        h.Test("modifiers parse in any order and any case", () =>
        {
            Harness.IsTrue(Hotkey.TryParse("ctrl+shift+f9", out Hotkey? a, out _), "lowercase");
            Harness.IsTrue(Hotkey.TryParse("Shift + Control + F9", out Hotkey? b, out _), "spaced, reordered");
            Harness.AreEqual(Hotkey.ModControl | Hotkey.ModShift, a!.Modifiers, "modifier flags");
            Harness.IsTrue(a.Equals(b), "same hotkey either way");
            Harness.AreEqual("Ctrl+Shift+F9", a.Display, "canonical display");
        });

        h.Test("letters, digits and named keys parse", () =>
        {
            Harness.IsTrue(Hotkey.TryParse("Alt+K", out Hotkey? k, out _), "letter");
            Harness.AreEqual((uint)'K', k!.VirtualKey, "letter code");
            Harness.IsTrue(Hotkey.TryParse("Numpad5", out Hotkey? n, out _), "numpad");
            Harness.AreEqual(0x65u, n!.VirtualKey, "numpad code");
            Harness.IsTrue(Hotkey.TryParse("PageDown", out _, out _), "named key");
        });

        h.Test("nonsense is rejected with a reason", () =>
        {
            Harness.IsFalse(Hotkey.TryParse("Ctrl", out _, out string e1), "modifier alone");
            Harness.IsTrue(e1.Length > 0, "explains why");
            Harness.IsFalse(Hotkey.TryParse("F8+F9", out _, out _), "two keys");
            Harness.IsFalse(Hotkey.TryParse("Banana", out _, out _), "unknown key");
            Harness.IsFalse(Hotkey.TryParse("", out _, out _), "empty");
            Harness.IsFalse(Hotkey.TryParse(null, out _, out _), "null");
        });

        h.Test("ParseOrDefault falls back to F8", () =>
        {
            Harness.IsTrue(Hotkey.Default.Equals(Hotkey.ParseOrDefault("nonsense")), "fallback");
            Harness.AreEqual("F12", Hotkey.ParseOrDefault("f12").Display, "good input is kept");
        });
    }

    private static void RegisterNumberInput(Harness h)
    {
        h.Section("NumberInput");

        h.Test("dots and commas both work as decimal points", () =>
        {
            Harness.IsTrue(NumberInput.TryParse("2.5", out double a), "dot parsed");
            Harness.AreClose(2.5, a, "dot value");
            Harness.IsTrue(NumberInput.TryParse("2,5", out double b), "comma parsed");
            Harness.AreClose(2.5, b, "comma value");
        });

        h.Test("surrounding spaces are ignored", () =>
        {
            Harness.IsTrue(NumberInput.TryParse("  30  ", out double v), "parsed");
            Harness.AreClose(30, v, "value");
        });

        h.Test("non-numbers are refused", () =>
        {
            Harness.IsFalse(NumberInput.TryParse("abc", out _), "letters");
            Harness.IsFalse(NumberInput.TryParse("", out _), "empty");
            Harness.IsFalse(NumberInput.TryParse(null, out _), "null");
            Harness.IsFalse(NumberInput.TryParse("   ", out _), "whitespace");
        });

        h.Test("TryParsePositive refuses zero and negatives", () =>
        {
            Harness.IsFalse(NumberInput.TryParsePositive("0", out _), "zero");
            Harness.IsFalse(NumberInput.TryParsePositive("-3", out _), "negative");
            Harness.IsTrue(NumberInput.TryParsePositive("0.0001", out _), "small positive");
        });

        h.Test("sens formatting keeps useful decimals without noise", () =>
        {
            Harness.AreEqual("0.5", NumberInput.FormatSens(0.5), "half");
            Harness.AreEqual("2.3715", NumberInput.FormatSens(2.37154), "four decimals above one");
            Harness.AreEqual("0.034567", NumberInput.FormatSens(0.034567), "more decimals when small");
            Harness.AreEqual("140.25", NumberInput.FormatSens(140.2537), "fewer decimals when large");
            Harness.AreEqual("-", NumberInput.FormatSens(double.NaN), "NaN");
        });

        h.Test("counts and percentages read plainly", () =>
        {
            Harness.AreEqual("9,449", NumberInput.FormatCounts(9449.4), "counts");
            Harness.AreEqual("26.1%", NumberInput.FormatPercent(0.2609), "percent");
        });
    }

    private static void RegisterExeIdentity(Harness h)
    {
        h.Section("ExeIdentity");

        h.Test("hashing a file matches the known SHA-256", () =>
        {
            string path = Path.Combine(Path.GetTempPath(), "sensconv-hash-" + Guid.NewGuid().ToString("n"));
            try
            {
                File.WriteAllText(path, "hello");
                Harness.AreEqual(
                    "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
                    ExeIdentity.Sha256File(path),
                    "sha256 of hello");
            }
            finally
            {
                File.Delete(path);
            }
        });

        h.Test("the same bytes in two places hash the same", () =>
        {
            string a = Path.Combine(Path.GetTempPath(), "sensconv-a-" + Guid.NewGuid().ToString("n"));
            string b = Path.Combine(Path.GetTempPath(), "sensconv-b-" + Guid.NewGuid().ToString("n"));
            try
            {
                File.WriteAllText(a, "same bytes");
                File.WriteAllText(b, "same bytes");
                Harness.AreEqual(ExeIdentity.Sha256File(a), ExeIdentity.Sha256File(b), "moved game still recognised");
            }
            finally
            {
                File.Delete(a);
                File.Delete(b);
            }
        });

        h.Test("build noise is stripped from the suggested name", () =>
        {
            Harness.AreEqual("Game", ExeIdentity.PrettyNameFromPath(@"C:\x\Game-Win64-Shipping.exe"), "unreal style");
            Harness.AreEqual("My Game", ExeIdentity.PrettyNameFromPath(@"C:\x\my_game.exe"), "underscores");
            Harness.AreEqual("DOOMEternal", ExeIdentity.PrettyNameFromPath(@"C:\x\DOOMEternal.exe"), "existing caps kept");
            Harness.AreEqual("x64", ExeIdentity.PrettyNameFromPath(@"C:\x\x64.exe"), "all-noise name falls back");
            Harness.AreEqual("Unnamed game", ExeIdentity.PrettyNameFromPath(""), "empty path");
        });
    }
}
