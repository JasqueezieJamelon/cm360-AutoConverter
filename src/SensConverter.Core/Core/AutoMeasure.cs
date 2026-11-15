namespace SensConverter.Core;

/// <summary>One movement the program wants made, in mouse counts.</summary>
/// <param name="Dx">Horizontal counts, positive is right.</param>
/// <param name="Dy">Vertical counts, positive is down.</param>
public sealed record AutoStep(int Dx, int Dy)
{
    /// <summary>Total counts in this step, whichever axis it is on.</summary>
    public int Counts => Math.Abs(Dx) + Math.Abs(Dy);
}

/// <summary>Where an automatic measurement has got to.</summary>
public enum AutoPhase
{
    /// <summary>Waiting for the first frame.</summary>
    NotStarted = 0,

    /// <summary>Finding a step size that suits this game, which could be anywhere from 0.01 to 50.</summary>
    Probing = 1,

    /// <summary>Turning, step by step, looking for the view to come back round.</summary>
    Sweeping = 2,

    /// <summary>Finished, with an answer.</summary>
    Done = 3,

    /// <summary>Gave up. <see cref="AutoSpinSession.Failure"/> says why, in words for the user.</summary>
    Failed = 4,
}

/// <summary>
/// The tuning numbers for automatic measurement, all in one place so they can be reasoned about
/// (and overridden in tests) rather than scattered through the logic as magic numbers.
/// </summary>
public sealed class AutoMeasureOptions
{
    /// <summary>Step sizes tried in turn until one moves the picture a useful amount.</summary>
    public int[] ProbeLadder { get; init; } = { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096 };

    /// <summary>A step that moves the picture less than this is too small to measure well.</summary>
    public double MinProbeShiftPixels { get; init; } = 8;

    /// <summary>A step that moves the picture more than this fraction of the frame is too coarse.</summary>
    public double MaxProbeShiftFraction { get; init; } = 0.35;

    /// <summary>How far each sweeping step should move the picture, as a fraction of the frame.</summary>
    public double SweepShiftFraction { get; init; } = 0.20;

    /// <summary>The smaller of the two steps used to check the game scales linearly.</summary>
    public double LinearityCheckSmallPixels { get; init; } = 14;

    /// <summary>The larger of the two, as a fraction of the frame.</summary>
    public double LinearityCheckLargeFraction { get; init; } = 0.30;

    /// <summary>
    /// How far apart those two steps' pixels-per-count may be before the game is called
    /// non-linear. Tight, because both are measured the same way, so there is little to make them
    /// differ other than the game itself.
    /// </summary>
    public double LinearityRatio { get; init; } = 1.15;

    /// <summary>
    /// How far the picture must have travelled before a match against the starting frame is
    /// believed. Without this, the very first steps would "match" the start and finish instantly.
    /// </summary>
    public double MinTravelBeforeWrapWidths { get; init; } = 1.5;

    /// <summary>
    /// A full 360 is between about 2.3 and 4.5 screen widths of panorama for any normal field of
    /// view. Past this much travel with no match, something is wrong.
    /// </summary>
    public double MaxTravelWidths { get; init; } = 8.0;

    /// <summary>Used only to show a progress bar: the typical panorama width for a 360.</summary>
    public double TypicalTravelWidths { get; init; } = 3.4;

    /// <summary>Safety net on the number of movements, in case nothing ever converges.</summary>
    public int MaxSteps { get; init; } = 400;

    /// <summary>
    /// Deciding the view has come back round ends the measurement and fixes the answer, so it is
    /// held to a higher standard than merely following the picture from one step to the next. A
    /// view that really has returned looks almost exactly like the start; a chance resemblance
    /// between two unrelated parts of the world does not.
    ///
    /// Getting this wrong is the worst failure available, because it produces a confident number
    /// that is simply wrong rather than an error the user can see.
    /// </summary>
    public double MinWrapScore { get; init; } = 0.80;

    /// <summary>The certainty a wrap match must also reach.</summary>
    public double MinWrapConfidence { get; init; } = 0.20;

    /// <summary>
    /// How many screen widths of panorama a full turn may plausibly be. Fixed by geometry: for any
    /// field of view between about 60 and 120 degrees a 360 is between roughly 1.8 and 5.5 screens
    /// wide, so an answer outside this range did not come from a real turn.
    /// </summary>
    public double MinPanoramaWidths { get; init; } = 1.6;

    /// <summary>The other end of that range.</summary>
    public double MaxPanoramaWidths { get; init; } = 6.5;

    /// <summary>
    /// The picture needs some texture to match on. A flat wall or a loading screen scores near
    /// zero and cannot be measured against.
    /// </summary>
    public double MinDetail { get; init; } = 2.0;

    /// <summary>
    /// How much the pixels-moved-per-count may vary between equal-sized sweeping steps. Looser
    /// than <see cref="LinearityRatio"/>, because perspective alone makes it wobble a little.
    /// </summary>
    public double NonLinearRatio { get; init; } = 1.5;

    public static AutoMeasureOptions Default { get; } = new();
}

/// <summary>
/// Measures how many mouse counts a game needs for a full 360, by turning the view itself and
/// watching the screen.
///
/// The idea avoids ever needing to know the game's field of view. The program turns the view in
/// steps, and after each step asks "does the screen look like where I started yet?". It only can
/// at one place: a whole turn later. Adding up the counts sent to get there gives counts per 360
/// directly, which is exactly what a hand-made spin measures.
///
/// Three details make it trustworthy. The step size is found by trial first, since a game's
/// sensitivity is unknown and could be anything. Two deliberately different-sized steps are then
/// compared, which is the only way to catch a game that applies its own mouse acceleration - the
/// model behind the whole program assumes it does not. And the last step almost never lands
/// exactly on the mark, so the leftover is measured in pixels and converted back into counts
/// rather than being ignored.
///
/// This class does no capturing and moves no mouse. It is handed brightness profiles and it asks
/// for movements, which makes the whole thing testable against a simulated game.
/// </summary>
public sealed class AutoSpinSession
{
    private readonly AutoMeasureOptions options;
    private readonly List<double> stepPixelsPerCount = new();

    private double[] reference = Array.Empty<double>();
    private double[] previous = Array.Empty<double>();
    private int width;

    private ProbeStage stage = ProbeStage.Ladder;
    private int probeIndex = -1;
    private int probeCounts;
    private double roughPixelsPerCount;
    private double smallStepPixelsPerCount;
    private double largeStepPixelsPerCount;

    private int sweepCounts;
    private int countsSent;
    private int steps;
    private double travelPixels;
    private AutoStep? pending;

    public AutoSpinSession(AutoMeasureOptions? options = null)
    {
        this.options = options ?? AutoMeasureOptions.Default;
    }

    /// <summary>The steps of working out a step size, before the real turning starts.</summary>
    private enum ProbeStage
    {
        /// <summary>Working up through the ladder of step sizes.</summary>
        Ladder,

        /// <summary>One corrective measurement, after a ladder rung overshot.</summary>
        Adjust,

        /// <summary>A deliberately small step, for the linearity check.</summary>
        Small,

        /// <summary>A deliberately large one, which also sets the final pixels-per-count.</summary>
        Large,
    }

    public AutoPhase Phase { get; private set; } = AutoPhase.NotStarted;

    /// <summary>Why it gave up, in words meant for the user. Null unless <see cref="Phase"/> is failed.</summary>
    public string? Failure { get; private set; }

    /// <summary>The answer: mouse counts for a full 360 at the sens the game is set to.</summary>
    public double Counts360 { get; private set; }

    /// <summary>How far the picture moves per mouse count, from the probing stage.</summary>
    public double PixelsPerCount { get; private set; }

    /// <summary>Total counts sent so far.</summary>
    public int CountsSent => countsSent;

    public int Steps => steps;

    /// <summary>Rough progress for a progress bar, 0 to 1. Only ever an estimate until it finishes.</summary>
    public double Progress
    {
        get
        {
            if (Phase == AutoPhase.Done)
            {
                return 1;
            }

            if (width <= 0 || travelPixels <= 0)
            {
                return 0;
            }

            return Math.Min(0.99, travelPixels / (options.TypicalTravelWidths * width));
        }
    }

    /// <summary>
    /// True when the game did not scale mouse movement in a straight line, which is what in-game
    /// mouse acceleration or smoothing looks like from the outside.
    ///
    /// This matters because the conversion the whole program is built on assumes a straight line.
    /// If this is set, the measured number is only right for movements the size of the ones used
    /// to measure it, and the user should be told so rather than handed a number that looks exact.
    /// </summary>
    public bool LooksNonLinear { get; private set; }

    /// <summary>
    /// How far apart the small and large check steps came out, as a ratio. 1.0 is a perfectly
    /// linear game. Zero when the check could not be made.
    /// </summary>
    public double LinearitySpread
        => smallStepPixelsPerCount > 0 && largeStepPixelsPerCount > 0
            ? Math.Max(smallStepPixelsPerCount, largeStepPixelsPerCount)
              / Math.Min(smallStepPixelsPerCount, largeStepPixelsPerCount)
            : 0;

    /// <summary>Starts the measurement from the frame the game is showing right now.</summary>
    public void Begin(double[] referenceProfile)
    {
        ArgumentNullException.ThrowIfNull(referenceProfile);

        if (referenceProfile.Length < ImageShift.MinOverlap * 2)
        {
            Fail("The captured picture is too small to measure.");
            return;
        }

        if (GrayFrame.Detail(referenceProfile) < options.MinDetail)
        {
            Fail("The screen is too plain to measure against. Face something with more detail in it - "
                 + "scenery or a textured wall rather than the sky or a blank surface.");
            return;
        }

        reference = (double[])referenceProfile.Clone();
        previous = (double[])referenceProfile.Clone();
        width = referenceProfile.Length;
        Phase = AutoPhase.Probing;
    }

    /// <summary>
    /// The next movement to make, or null when there is nothing left to do. The caller sends it,
    /// waits for the game to draw, captures, and hands the new profile to <see cref="Observe"/>.
    /// </summary>
    public AutoStep? NextStep()
    {
        if (Phase is AutoPhase.Done or AutoPhase.Failed or AutoPhase.NotStarted)
        {
            return null;
        }

        if (steps >= options.MaxSteps)
        {
            Fail("Gave up after too many movements without the view coming back round.");
            return null;
        }

        if (Phase == AutoPhase.Probing)
        {
            if (!TryPlanProbe())
            {
                return null;
            }

            pending = new AutoStep(probeCounts, 0);
            return pending;
        }

        pending = new AutoStep(sweepCounts, 0);
        return pending;
    }

    /// <summary>Takes the frame that resulted from the last step returned by <see cref="NextStep"/>.</summary>
    public void Observe(double[] profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (Phase is AutoPhase.Done or AutoPhase.Failed or AutoPhase.NotStarted || pending is null)
        {
            return;
        }

        if (profile.Length != width)
        {
            Fail("The captured picture changed size part way through. Keep the game window as it was.");
            return;
        }

        countsSent += pending.Counts;
        steps++;

        int searchRange = Math.Max(ImageShift.MinOverlap, width / 2);
        ShiftMatch consecutive = ImageShift.Find(previous, profile, searchRange);
        previous = (double[])profile.Clone();

        if (consecutive.IsReliable)
        {
            travelPixels += Math.Abs(consecutive.Shift);
        }

        if (Phase == AutoPhase.Probing)
        {
            HandleProbe(consecutive);
            return;
        }

        HandleSweep(profile, consecutive);
    }

    /// <summary>Decides what size the next probing step should be.</summary>
    private bool TryPlanProbe()
    {
        switch (stage)
        {
            case ProbeStage.Ladder:
                probeIndex++;
                if (probeIndex >= options.ProbeLadder.Length)
                {
                    Fail("Could not find a mouse movement this game responds to in a measurable way. "
                         + "Check the game is in the foreground and is not paused.");
                    return false;
                }

                probeCounts = options.ProbeLadder[probeIndex];
                return true;

            case ProbeStage.Adjust:
                // probeCounts was already worked out when the ladder rung overshot.
                return true;

            case ProbeStage.Small:
                probeCounts = CountsForPixels(options.LinearityCheckSmallPixels);
                return true;

            case ProbeStage.Large:
                probeCounts = CountsForPixels(options.LinearityCheckLargeFraction * width);
                return true;

            default:
                Fail("Internal error working out a step size.");
                return false;
        }
    }

    private int CountsForPixels(double pixels)
        => roughPixelsPerCount > 0 ? Math.Max(1, (int)Math.Round(pixels / roughPixelsPerCount)) : 1;

    /// <summary>
    /// Works up the ladder of step sizes until the picture moves a useful amount, then measures
    /// once small and once large so the two can be compared.
    /// </summary>
    private void HandleProbe(ShiftMatch match)
    {
        double moved = Math.Abs(match.Shift);
        double maxShift = options.MaxProbeShiftFraction * width;

        switch (stage)
        {
            case ProbeStage.Ladder:
                if (!match.IsReliable || moved < options.MinProbeShiftPixels)
                {
                    // Either the step was too small to see or the picture has nothing to match on.
                    // Going up the ladder settles it: a bigger step either shows up or never will.
                    return;
                }

                roughPixelsPerCount = moved / probeCounts;

                if (moved > maxShift)
                {
                    // Overshot: scale down to land in the useful range and measure once more.
                    probeCounts = CountsForPixels(options.SweepShiftFraction * width);
                    stage = ProbeStage.Adjust;
                    return;
                }

                stage = ProbeStage.Small;
                return;

            case ProbeStage.Adjust:
                if (!match.IsReliable || moved < options.MinProbeShiftPixels)
                {
                    Fail("Could not follow the picture reliably while turning. The view may have too "
                         + "little detail, or too much of it moving on its own.");
                    return;
                }

                roughPixelsPerCount = moved / probeCounts;
                stage = ProbeStage.Small;
                return;

            case ProbeStage.Small:
                // A failed small step only costs the linearity check, so carry on either way.
                if (match.IsReliable && moved >= options.MinProbeShiftPixels)
                {
                    smallStepPixelsPerCount = moved / probeCounts;
                }

                stage = ProbeStage.Large;
                return;

            case ProbeStage.Large:
                if (match.IsReliable && moved >= options.MinProbeShiftPixels)
                {
                    largeStepPixelsPerCount = moved / probeCounts;
                    PixelsPerCount = largeStepPixelsPerCount;
                }
                else
                {
                    PixelsPerCount = roughPixelsPerCount;
                }

                if (PixelsPerCount <= 0)
                {
                    Fail("Could not work out how far the view turns per unit of mouse movement.");
                    return;
                }

                // The same movement, asked for in two very different sizes, should move the
                // picture proportionally. When it does not, the game is doing its own scaling.
                double spread = LinearitySpread;
                if (spread > 0 && spread > options.LinearityRatio)
                {
                    LooksNonLinear = true;
                }

                sweepCounts = Math.Max(1, (int)Math.Round(options.SweepShiftFraction * width / PixelsPerCount));
                Phase = AutoPhase.Sweeping;
                return;
        }
    }

    /// <summary>
    /// Turns in even steps, watching for the starting view to reappear. Once it does, the leftover
    /// offset in pixels is converted back into counts so the answer is not limited by step size.
    /// </summary>
    private void HandleSweep(double[] profile, ShiftMatch consecutive)
    {
        if (consecutive.IsReliable && consecutive.Shift != 0)
        {
            stepPixelsPerCount.Add(Math.Abs(consecutive.Shift) / Math.Max(1, sweepCounts));
        }
        else
        {
            // One unreadable frame is not fatal - something may have flashed on screen - so assume
            // the step moved as much as asked and carry on.
            travelPixels += sweepCounts * PixelsPerCount;
        }

        if (stepPixelsPerCount.Count >= 3)
        {
            double min = stepPixelsPerCount.Min();
            double max = stepPixelsPerCount.Max();
            if (min > 0 && max / min > options.NonLinearRatio)
            {
                LooksNonLinear = true;
            }
        }

        if (travelPixels > options.MaxTravelWidths * width)
        {
            Fail("Turned well past a full circle without the starting view coming back. The game "
                 + "may not turn in a straight line with the mouse, or the view may have drifted.");
            return;
        }

        if (travelPixels < options.MinTravelBeforeWrapWidths * width)
        {
            return;
        }

        int searchRange = Math.Max(ImageShift.MinOverlap, (int)Math.Round(options.MaxProbeShiftFraction * width));
        ShiftMatch wrap = ImageShift.Find(reference, profile, searchRange);

        if (!wrap.IsReliable || wrap.Score < options.MinWrapScore || wrap.Confidence < options.MinWrapConfidence)
        {
            return;
        }

        // The starting view is back on screen. Where exactly it sits says how far short of - or
        // past - a full turn this is, and that converts straight back into counts.
        double correction = wrap.Shift / PixelsPerCount;
        double total = countsSent + correction;

        if (total <= 0)
        {
            Fail("The measurement came out as a negative turn, which cannot be right.");
            return;
        }

        // Last check, from geometry alone: a full turn is only ever a few screen widths of
        // scenery. An answer outside that came from two unrelated places happening to look alike,
        // not from the view returning, and it would be wrong without looking wrong.
        double panoramaWidths = total * PixelsPerCount / width;
        if (panoramaWidths < options.MinPanoramaWidths || panoramaWidths > options.MaxPanoramaWidths)
        {
            return;
        }

        Counts360 = total;
        Phase = AutoPhase.Done;
    }

    private void Fail(string reason)
    {
        Phase = AutoPhase.Failed;
        Failure = reason;
    }
}
