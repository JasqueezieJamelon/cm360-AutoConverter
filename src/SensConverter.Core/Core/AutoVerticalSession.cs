namespace SensConverter.Core;

/// <summary>
/// Measures a game's vertical sensitivity relative to its horizontal one.
///
/// Vertical cannot be measured the way horizontal is, because there is no vertical 360 - every
/// game stops you looking further up than straight up. So this measures a ratio instead.
///
/// It nudges the view a small amount sideways, then the same amount up and down, and compares how
/// far the picture moved each time. Near the middle of the screen a degree of pitch and a degree
/// of yaw move the picture by the same number of pixels, so the ratio of
/// pixels-per-count-vertically to pixels-per-count-horizontally *is* the ratio of the two
/// sensitivities. The field of view cancels out and never has to be known.
///
/// Multiply that ratio by the horizontal degrees-per-count, which the 360 measurement already
/// gave, and the vertical axis is solved in the same units as the horizontal one.
///
/// Every movement is paired with one back the other way, so the view ends up where it started and
/// nothing drifts.
/// </summary>
public sealed class AutoVerticalSession
{
    /// <summary>How far each nudge should move the picture, as a fraction of the frame. Small, to
    /// stay near the middle of the screen where the two axes are directly comparable.</summary>
    public const double StepShiftFraction = 0.10;

    /// <summary>
    /// Up and down should move the picture by the same amount. If they do not, the view ran into
    /// the game's look-up or look-down limit and the measurement is worthless.
    /// </summary>
    public const double MaxDirectionMismatch = 0.25;

    /// <summary>Said whenever the view turns out to be jammed against its up or down limit.</summary>
    private const string LimitMessage =
        "Looking up and looking down did not move the view by the same amount, which means it hit the "
        + "game's up or down limit. Face the horizon, so there is room to look both ways, and try again.";

    private static readonly (int Dx, int Dy)[] Pattern =
    {
        (1, 0),   // right
        (-1, 0),  // and back
        (0, 1),   // down
        (0, -1),  // and back up
    };

    private readonly AutoMeasureOptions options;
    private readonly double pixelsPerCount;
    private readonly int rounds;

    private readonly List<double> horizontal = new();
    private readonly List<double> down = new();
    private readonly List<double> up = new();

    private double[] previousColumns = Array.Empty<double>();
    private double[] previousRows = Array.Empty<double>();
    private int width;
    private int height;
    private int stepCounts;
    private int moveIndex;
    private int verticalStalls;
    private int horizontalStalls;
    private (int Dx, int Dy)? pending;

    /// <param name="pixelsPerCount">
    /// How far the picture moves per mouse count, as found during the horizontal measurement. Used
    /// only to pick a sensible nudge size.
    /// </param>
    /// <param name="rounds">How many times to repeat the four-movement pattern.</param>
    public AutoVerticalSession(double pixelsPerCount, int rounds = 3, AutoMeasureOptions? options = null)
    {
        if (pixelsPerCount <= 0 || double.IsNaN(pixelsPerCount) || double.IsInfinity(pixelsPerCount))
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerCount), pixelsPerCount, "Must be positive.");
        }

        this.pixelsPerCount = pixelsPerCount;
        this.rounds = Math.Clamp(rounds, 1, 20);
        this.options = options ?? AutoMeasureOptions.Default;
    }

    public AutoPhase Phase { get; private set; } = AutoPhase.NotStarted;

    public string? Failure { get; private set; }

    /// <summary>
    /// How much more sensitive vertical is than horizontal, at whatever sens values the game is
    /// currently set to. 1.0 means the two axes move alike.
    /// </summary>
    public double VerticalRatio { get; private set; }

    /// <summary>Total counts of movement sent, for the record.</summary>
    public int CountsSent { get; private set; }

    public double Progress => Phase == AutoPhase.Done ? 1 : Math.Min(0.99, (double)moveIndex / TotalMoves);

    private int TotalMoves => rounds * Pattern.Length;

    public void Begin(double[] columnProfile, double[] rowProfile)
    {
        ArgumentNullException.ThrowIfNull(columnProfile);
        ArgumentNullException.ThrowIfNull(rowProfile);

        if (columnProfile.Length < ImageShift.MinOverlap * 2 || rowProfile.Length < ImageShift.MinOverlap * 2)
        {
            Fail("The captured picture is too small to measure.");
            return;
        }

        if (GrayFrame.Detail(columnProfile) < options.MinDetail
            || GrayFrame.Detail(rowProfile) < options.MinDetail)
        {
            Fail("The screen is too plain to measure against. Look at the horizon with some "
                 + "scenery in view rather than at the sky or a blank wall.");
            return;
        }

        previousColumns = (double[])columnProfile.Clone();
        previousRows = (double[])rowProfile.Clone();
        width = columnProfile.Length;
        height = rowProfile.Length;

        // Sized off the shorter axis so the same nudge is comfortably measurable both ways.
        int shorter = Math.Min(width, height);
        stepCounts = Math.Max(1, (int)Math.Round(StepShiftFraction * shorter / pixelsPerCount));
        Phase = AutoPhase.Sweeping;
    }

    public AutoStep? NextStep()
    {
        if (Phase != AutoPhase.Sweeping)
        {
            return null;
        }

        if (moveIndex >= TotalMoves)
        {
            Finish();
            return null;
        }

        (int dx, int dy) = Pattern[moveIndex % Pattern.Length];
        pending = (dx, dy);
        return new AutoStep(dx * stepCounts, dy * stepCounts);
    }

    /// <summary>
    /// Takes the frame that resulted from the last step. Both profiles are needed because the
    /// pattern alternates between axes.
    /// </summary>
    public void Observe(double[] columnProfile, double[] rowProfile)
    {
        ArgumentNullException.ThrowIfNull(columnProfile);
        ArgumentNullException.ThrowIfNull(rowProfile);

        if (Phase != AutoPhase.Sweeping || pending is null)
        {
            return;
        }

        if (columnProfile.Length != width || rowProfile.Length != height)
        {
            Fail("The captured picture changed size part way through. Keep the game window as it was.");
            return;
        }

        (int dx, int dy) = pending.Value;
        CountsSent += stepCounts;
        moveIndex++;

        bool isHorizontal = dx != 0;
        double[] before = isHorizontal ? previousColumns : previousRows;
        double[] after = isHorizontal ? columnProfile : rowProfile;
        int searchRange = Math.Max(ImageShift.MinOverlap, before.Length / 2);

        ShiftMatch match = ImageShift.Find(before, after, searchRange);

        previousColumns = (double[])columnProfile.Clone();
        previousRows = (double[])rowProfile.Clone();

        if (!match.IsReliable || Math.Abs(match.Shift) < options.MinProbeShiftPixels)
        {
            // Which axis stalled matters. If sideways reads fine but up and down does not move,
            // the view is jammed against the game's look limit rather than being unreadable - a
            // different problem with a different fix, so it gets its own message.
            if (isHorizontal)
            {
                horizontalStalls++;
            }
            else
            {
                verticalStalls++;
            }

            if (verticalStalls >= 2 && horizontal.Count > 0 && horizontalStalls == 0)
            {
                Fail(LimitMessage);
                return;
            }

            // A few unreadable frames are survivable; most of them being unreadable is not.
            if (verticalStalls + horizontalStalls > TotalMoves / 2)
            {
                Fail("Could not follow the picture well enough to measure the vertical axis. Try "
                     + "facing something with more detail in it.");
            }

            return;
        }

        double moved = Math.Abs(match.Shift);

        if (isHorizontal)
        {
            horizontal.Add(moved);
        }
        else if (dy > 0)
        {
            down.Add(moved);
        }
        else
        {
            up.Add(moved);
        }

        if (moveIndex >= TotalMoves)
        {
            Finish();
        }
    }

    private void Finish()
    {
        if (Phase != AutoPhase.Sweeping)
        {
            return;
        }

        if (horizontal.Count == 0 || down.Count == 0 || up.Count == 0)
        {
            Fail("Did not get a usable reading on both axes.");
            return;
        }

        double downMean = down.Average();
        double upMean = up.Average();
        double larger = Math.Max(downMean, upMean);

        if (larger > 0 && Math.Abs(downMean - upMean) / larger > MaxDirectionMismatch)
        {
            Fail(LimitMessage);
            return;
        }

        double horizontalMean = horizontal.Average();
        if (horizontalMean <= 0)
        {
            Fail("Did not get a usable sideways reading to compare the vertical one against.");
            return;
        }

        VerticalRatio = (downMean + upMean) / 2 / horizontalMean;
        Phase = AutoPhase.Done;
    }

    private void Fail(string reason)
    {
        Phase = AutoPhase.Failed;
        Failure = reason;
    }
}
