using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SensConverter.App.Input;
using SensConverter.Core;

namespace SensConverter.App;

/// <summary>
/// Checks the two things automatic measurement depends on, separately, and writes down what
/// happened: can this program move the mouse, and can it see the screen.
///
/// When a measurement fails, those two look identical from the outside - the picture simply never
/// changed. Testing them one at a time says which one is broken.
///
/// Run with --selftest. No window appears; it writes selftest.txt and exits.
/// </summary>
internal static partial class SelfTest
{
    public static void Run(string? dataDirectory)
    {
        StringBuilder report = new();
        report.AppendLine("Sensitivity Converter - self test");
        report.AppendLine(DateTimeOffset.Now.ToString("u", CultureInfo.InvariantCulture));
        report.AppendLine();

        TestMouse(report);
        report.AppendLine();
        TestCapture(report);

        string directory = dataDirectory ?? AppContext.BaseDirectory;
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "selftest.txt"), report.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing else to do without a window to complain in.
        }
    }

    /// <summary>
    /// Can this program move the mouse? Moves the pointer by a known amount and asks Windows where
    /// it ended up. If the pointer does not move, nothing downstream can possibly work.
    /// </summary>
    private static void TestMouse(StringBuilder report)
    {
        report.AppendLine("-- moving the mouse --");
        report.AppendLine($"pointer acceleration: {(SyntheticMouse.IsPointerAccelerationOn() ? "ON" : "off")}");

        if (!GetCursorPos(out PointL before))
        {
            report.AppendLine("FAILED: Windows would not say where the pointer is.");
            return;
        }

        const int distance = 200;
        bool sent = SyntheticMouse.Move(distance, 0);
        Thread.Sleep(120);

        if (!GetCursorPos(out PointL after))
        {
            report.AppendLine("FAILED: Windows would not say where the pointer ended up.");
            return;
        }

        int moved = after.X - before.X;
        report.AppendLine($"SendInput accepted: {sent}");
        report.AppendLine($"asked for {distance} counts right, pointer moved {moved} px "
                          + $"(from {before.X},{before.Y} to {after.X},{after.Y})");

        // Put it back, so the test does not leave the pointer somewhere surprising.
        SyntheticMouse.Move(-moved, 0);

        if (moved == 0)
        {
            report.AppendLine("RESULT: the pointer did not move at all. Mouse injection is being");
            report.AppendLine("blocked - most likely by something running as administrator.");
        }
        else if (Math.Abs(moved - distance) > distance * 0.25)
        {
            report.AppendLine("RESULT: the pointer moved, but not by the amount asked for. Windows");
            report.AppendLine("is scaling the movement, which would make measurements wrong.");
        }
        else
        {
            report.AppendLine("RESULT: mouse movement works.");
        }
    }

    /// <summary>
    /// Can this program see the screen? Captures twice, a moment apart, and reports how much detail
    /// there is and whether anything changed.
    /// </summary>
    private static void TestCapture(StringBuilder report)
    {
        report.AppendLine("-- seeing the screen --");
        report.AppendLine("foreground: " + ScreenCapture.DescribeForeground());

        GrayFrame? first = ScreenCapture.CaptureForeground(out string error);
        if (first is null)
        {
            report.AppendLine("FAILED: " + error);
            return;
        }

        double[] firstProfile = first.ColumnProfile(1.0);
        report.AppendLine($"captured {first.Width}x{first.Height}, detail {GrayFrame.Detail(firstProfile):F2}");

        // Matched against itself. Nothing can have changed in between, so this measures the
        // matching alone, on a real picture, with the content held still.
        ShiftMatch itself = ImageShift.Find(firstProfile, firstProfile, Math.Max(ImageShift.MinOverlap, first.Width / 2));
        report.AppendLine($"this frame against itself: shift {itself.Shift:F2} px, score {itself.Score:F3}, "
                          + $"confidence {itself.Confidence:F3}, "
                          + $"{(itself.IsReliable ? "trusted" : "NOT TRUSTED - the matching is too fussy")}");

        Thread.Sleep(300);

        GrayFrame? second = ScreenCapture.CaptureForeground(out error);
        if (second is null)
        {
            report.AppendLine("FAILED on the second capture: " + error);
            return;
        }

        if (second.Width != first.Width)
        {
            report.AppendLine("the window changed size between captures");
            return;
        }

        ShiftMatch match = ImageShift.Find(
            firstProfile,
            second.ColumnProfile(1.0),
            Math.Max(ImageShift.MinOverlap, first.Width / 2));

        report.AppendLine($"between two captures 300 ms apart: shift {match.Shift:F2} px, "
                          + $"score {match.Score:F3}, confidence {match.Confidence:F3}");

        if (GrayFrame.Detail(firstProfile) < 2.0)
        {
            report.AppendLine("RESULT: the captured picture is nearly featureless. Either the");
            report.AppendLine("capture is not seeing real content, or the screen really is blank.");
        }
        else
        {
            report.AppendLine("RESULT: capture works, and the picture has enough detail to match on.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out PointL point);
}
