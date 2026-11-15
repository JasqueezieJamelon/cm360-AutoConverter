using SensConverter.Core;

namespace SensConverter.App.Input;

/// <summary>What one automatic measurement produced.</summary>
/// <param name="Counts360">Mouse counts for a full turn, or zero when it failed.</param>
/// <param name="PixelsPerCount">How far the picture moved per count, for the vertical stage.</param>
/// <param name="VerticalRatio">
/// How much more sensitive the vertical axis is than the horizontal one, or zero when vertical was
/// not asked for or could not be measured.
/// </param>
/// <param name="LooksNonLinear">The game did not scale mouse movement in a straight line.</param>
/// <param name="Error">Null on success; otherwise what went wrong, in words for the user.</param>
internal sealed record AutoMeasureResult(
    double Counts360,
    double PixelsPerCount,
    double VerticalRatio,
    bool LooksNonLinear,
    string? Error)
{
    public bool Ok => Error is null && Counts360 > 0;

    public bool HasVertical => VerticalRatio > 0;

    public static AutoMeasureResult Failed(string error) => new(0, 0, 0, false, error);
}

/// <summary>
/// Runs an automatic measurement against the game that is actually in front: captures the screen,
/// moves the mouse, and feeds the results to the session logic in the core.
///
/// Meant to be called on a background thread. It sleeps between movements waiting for the game to
/// draw, so running it on the UI thread would freeze the window.
/// </summary>
internal static class AutoMeasureRunner
{
    /// <summary>How long to wait after moving before expecting the game to have drawn the new view.</summary>
    public const int SettleMilliseconds = 45;

    /// <summary>How many extra captures to take while waiting for the view to stop changing.</summary>
    public const int MaxSettleAttempts = 6;

    /// <summary>Two captures this close together count as the same view, so it has settled.</summary>
    public const double SettledPixels = 1.0;

    /// <param name="measureVertical">Also measure the vertical axis, after the turn.</param>
    /// <param name="onProgress">Called with a fraction from 0 to 1. Comes from a background thread.</param>
    public static AutoMeasureResult Run(
        bool measureVertical,
        Action<double>? onProgress = null,
        CancellationToken cancellation = default,
        MeasurementDiagnostics? diagnostics = null)
    {
        diagnostics?.Note("foreground at start: " + ScreenCapture.DescribeForeground());
        diagnostics?.Note($"pointer acceleration: {(SyntheticMouse.IsPointerAccelerationOn() ? "ON" : "off")}");

        GrayFrame? first = CaptureSettled(out string error);
        if (first is null)
        {
            diagnostics?.Note("first capture failed: " + error);
            return AutoMeasureResult.Failed(error);
        }

        diagnostics?.Keep("start", first);
        double[] previousProfile = first.ColumnProfile(1.0);
        diagnostics?.Note(
            $"captured {first.Width}x{first.Height}, detail {GrayFrame.Detail(previousProfile):F2}");

        AutoSpinSession spin = new();
        spin.Begin(previousProfile);

        if (spin.Phase == AutoPhase.Failed)
        {
            diagnostics?.Note("could not start: " + spin.Failure);
            return AutoMeasureResult.Failed(spin.Failure ?? "Could not start measuring.");
        }

        while (true)
        {
            if (cancellation.IsCancellationRequested)
            {
                return AutoMeasureResult.Failed("Cancelled.");
            }

            AutoStep? step = spin.NextStep();
            if (step is null)
            {
                break;
            }

            if (!SyntheticMouse.Move(step.Dx, step.Dy))
            {
                diagnostics?.Note("SendInput was refused");
                return AutoMeasureResult.Failed(
                    "Windows would not let the mouse be moved. This usually means the game is "
                    + "running as administrator - either run this program as administrator too, or "
                    + "measure the game by hand.");
            }

            GrayFrame? frame = CaptureSettled(out error);
            if (frame is null)
            {
                diagnostics?.Note("capture failed mid-run: " + error);
                return AutoMeasureResult.Failed(error);
            }

            double[] profile = frame.ColumnProfile(1.0);

            if (diagnostics is not null)
            {
                // Measured here as well as inside the session, so the log says what the picture
                // did independently of what the session made of it.
                ShiftMatch seen = ImageShift.Find(
                    previousProfile, profile, Math.Max(ImageShift.MinOverlap, frame.Width / 2));

                diagnostics.Note(
                    $"sent {step.Dx,6} counts -> picture moved {seen.Shift,8:F2} px "
                    + $"(score {seen.Score:F2}, confidence {seen.Confidence:F2}, "
                    + $"{(seen.IsReliable ? "reliable" : "NOT reliable")})");

                diagnostics.Keep($"after-{step.Dx}", frame);
            }

            previousProfile = profile;
            spin.Observe(profile);
            onProgress?.Invoke(spin.Progress * (measureVertical ? 0.8 : 1.0));
        }

        if (spin.Phase != AutoPhase.Done)
        {
            diagnostics?.Note("foreground at end: " + ScreenCapture.DescribeForeground());
            diagnostics?.Note("gave up: " + spin.Failure);
        }

        if (spin.Phase != AutoPhase.Done)
        {
            return AutoMeasureResult.Failed(spin.Failure ?? "The measurement did not finish.");
        }

        double verticalRatio = 0;
        if (measureVertical)
        {
            verticalRatio = RunVertical(spin.PixelsPerCount, onProgress, cancellation, out string? verticalError);
            if (verticalError is not null)
            {
                return new AutoMeasureResult(
                    spin.Counts360, spin.PixelsPerCount, 0, spin.LooksNonLinear, verticalError);
            }
        }

        onProgress?.Invoke(1.0);
        return new AutoMeasureResult(
            spin.Counts360, spin.PixelsPerCount, verticalRatio, spin.LooksNonLinear, null);
    }

    /// <summary>The vertical stage: small paired nudges, compared against sideways ones.</summary>
    private static double RunVertical(
        double pixelsPerCount,
        Action<double>? onProgress,
        CancellationToken cancellation,
        out string? error)
    {
        error = null;

        GrayFrame? first = CaptureSettled(out string captureError);
        if (first is null)
        {
            error = captureError;
            return 0;
        }

        AutoVerticalSession vertical = new(pixelsPerCount);
        vertical.Begin(first.ColumnProfile(1.0), first.RowProfile(1.0));

        if (vertical.Phase == AutoPhase.Failed)
        {
            error = vertical.Failure;
            return 0;
        }

        while (true)
        {
            if (cancellation.IsCancellationRequested)
            {
                error = "Cancelled.";
                return 0;
            }

            AutoStep? step = vertical.NextStep();
            if (step is null)
            {
                break;
            }

            if (!SyntheticMouse.Move(step.Dx, step.Dy))
            {
                error = "Windows would not let the mouse be moved part way through.";
                return 0;
            }

            GrayFrame? frame = CaptureSettled(out captureError);
            if (frame is null)
            {
                error = captureError;
                return 0;
            }

            vertical.Observe(frame.ColumnProfile(1.0), frame.RowProfile(1.0));
            onProgress?.Invoke(0.8 + (vertical.Progress * 0.2));
        }

        if (vertical.Phase != AutoPhase.Done)
        {
            error = vertical.Failure ?? "The vertical measurement did not finish.";
            return 0;
        }

        return vertical.VerticalRatio;
    }

    /// <summary>
    /// Captures once the view has stopped changing.
    ///
    /// Games do not draw the instant the mouse moves, and a frame captured too early shows the old
    /// view. Rather than guess a delay that suits every machine, this captures repeatedly until two
    /// in a row look the same, which means the game has caught up.
    /// </summary>
    private static GrayFrame? CaptureSettled(out string error)
    {
        Thread.Sleep(SettleMilliseconds);

        GrayFrame? previous = ScreenCapture.CaptureForeground(out error);
        if (previous is null)
        {
            return null;
        }

        for (int attempt = 0; attempt < MaxSettleAttempts; attempt++)
        {
            Thread.Sleep(SettleMilliseconds / 2);

            GrayFrame? current = ScreenCapture.CaptureForeground(out error);
            if (current is null)
            {
                return null;
            }

            if (current.Width != previous.Width)
            {
                previous = current;
                continue;
            }

            ShiftMatch match = ImageShift.Find(
                previous.ColumnProfile(1.0),
                current.ColumnProfile(1.0),
                Math.Max(ImageShift.MinOverlap, current.Width / 4));

            previous = current;

            if (match.IsReliable && Math.Abs(match.Shift) <= SettledPixels)
            {
                return current;
            }
        }

        // Still moving after all that. The scene itself may be animated, which the matching copes
        // with, so this is worth continuing with rather than treating as a failure.
        return previous;
    }
}
