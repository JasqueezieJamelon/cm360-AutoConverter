using SensConverter.Core;

namespace SensConverter.Tests;

/// <summary>
/// A pretend game, so automatic measurement can be tested without a game.
///
/// The world is a strip of scenery wrapped into a circle: 360 degrees of it, and the screen shows
/// a window onto it. Turning right slides the window right, so the picture slides left, exactly
/// like a real game. Looking up and down slides a second strip vertically.
///
/// The model is deliberately a simple cylinder - a real game's perspective stretches the picture
/// towards the edges of the screen. That difference does not matter here, because what is being
/// tested is the measuring logic: does it find a step size that suits the game, does it notice the
/// view coming back round, and does it convert the leftover into counts correctly. The scenery
/// itself is generated from a fixed seed, so a failure is always reproducible.
/// </summary>
internal sealed class FakeGame
{
    private readonly double[] panorama;
    private readonly double[] verticalStrip;
    private readonly int width;
    private readonly int height;

    /// <summary>Where the window sits on the panorama, in panorama samples.</summary>
    private double yawPixels;

    /// <summary>Where the window sits on the vertical strip, in samples from its middle.</summary>
    private double pitchPixels;

    /// <param name="counts360">The answer the measurement is supposed to find.</param>
    /// <param name="verticalRatio">How much more the view pitches than it yaws, per count.</param>
    /// <param name="pitchLimitPixels">
    /// How far the view can pitch before the game stops it, as in looking straight up. Generous by
    /// default so it never interferes.
    /// </param>
    /// <param name="accelerationExponent">
    /// 1.0 is a well-behaved game. Above that, a bigger movement turns proportionally further -
    /// which is what in-game mouse acceleration does, and what the measurement should notice.
    /// </param>
    public FakeGame(
        double counts360,
        double verticalRatio = 1.0,
        int width = 480,
        int height = 270,
        double pitchLimitPixels = 1e9,
        double accelerationExponent = 1.0,
        double detail = 40,
        double panoramaWidths = 3.3,
        int seed = 12345)
    {
        if (counts360 <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(counts360));
        }

        Counts360 = counts360;
        VerticalRatio = verticalRatio;
        PitchLimitPixels = pitchLimitPixels;
        AccelerationExponent = accelerationExponent;
        this.width = width;
        this.height = height;

        // A 360 of panorama is about 3.3 screen widths for a normal field of view. A test can
        // stretch it, which simulates a view that never does come back round.
        PanoramaWidth = (int)Math.Round(width * panoramaWidths);
        // How broad the shapes are, tied to what fills the screen rather than to the array length,
        // because that is the scale the matching actually works at.
        panorama = Scenery(PanoramaWidth, detail, seed, broadness: Math.Max(2, width / 30));
        verticalStrip = Scenery(height * 8, detail, seed + 977, broadness: Math.Max(2, height / 30));
        pitchPixels = verticalStrip.Length / 2.0;
    }

    public double Counts360 { get; }

    public double VerticalRatio { get; }

    public double PitchLimitPixels { get; }

    public double AccelerationExponent { get; }

    public int PanoramaWidth { get; }

    /// <summary>Pixels the picture moves per mouse count, which is what the prober should find.</summary>
    public double PixelsPerCount => PanoramaWidth / Counts360;

    public int Moves { get; private set; }

    /// <summary>Applies a movement, the way a game would.</summary>
    public void Move(AutoStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        Moves++;

        if (step.Dx != 0)
        {
            yawPixels += Scale(step.Dx) * PixelsPerCount;
            yawPixels = Wrap(yawPixels, PanoramaWidth);
        }

        if (step.Dy != 0)
        {
            double middle = verticalStrip.Length / 2.0;
            double wanted = pitchPixels + (Scale(step.Dy) * PixelsPerCount * VerticalRatio);

            // Games stop you looking further than straight up or straight down.
            double lowest = middle - PitchLimitPixels;
            double highest = middle + PitchLimitPixels;
            pitchPixels = Math.Clamp(wanted, Math.Max(0, lowest), Math.Min(verticalStrip.Length - height - 1, highest));
        }
    }

    /// <summary>What the brightness of each column looks like right now.</summary>
    public double[] ColumnProfile()
    {
        double[] profile = new double[width];
        for (int x = 0; x < width; x++)
        {
            profile[x] = Sample(panorama, yawPixels + x, wrap: true);
        }

        return profile;
    }

    /// <summary>What the brightness of each row looks like right now.</summary>
    public double[] RowProfile()
    {
        double[] profile = new double[height];
        for (int y = 0; y < height; y++)
        {
            profile[y] = Sample(verticalStrip, pitchPixels + y, wrap: false);
        }

        return profile;
    }

    /// <summary>In-game mouse acceleration, when the test asks for it.</summary>
    private double Scale(int counts)
    {
        if (Math.Abs(AccelerationExponent - 1.0) < 1e-12)
        {
            return counts;
        }

        double magnitude = Math.Pow(Math.Abs(counts), AccelerationExponent);
        return counts < 0 ? -magnitude : magnitude;
    }

    /// <summary>Linear interpolation, so fractional positions are handled like a real picture.</summary>
    private static double Sample(double[] data, double position, bool wrap)
    {
        double at = wrap ? Wrap(position, data.Length) : Math.Clamp(position, 0, data.Length - 1);
        int low = (int)Math.Floor(at);
        int high = low + 1;
        double fraction = at - low;

        if (wrap)
        {
            low = (int)Wrap(low, data.Length);
            high = (int)Wrap(high, data.Length);
        }
        else
        {
            low = Math.Clamp(low, 0, data.Length - 1);
            high = Math.Clamp(high, 0, data.Length - 1);
        }

        return (data[low] * (1 - fraction)) + (data[high] * fraction);
    }

    private static double Wrap(double value, int length)
    {
        double wrapped = value % length;
        return wrapped < 0 ? wrapped + length : wrapped;
    }

    /// <summary>
    /// Scenery built the way a real view is: a few broad shapes - hills, walls, blocks of light and
    /// shade - with finer texture laid over them.
    ///
    /// The broad shapes matter and are not decoration. They are what gives a real picture a wide
    /// match peak, and a simulated game made only of fine noise has an unnaturally sharp one. A
    /// test built on noise alone passed happily while the real thing failed on every game, because
    /// the matching treated the slope of that wide peak as a rival and trusted nothing.
    ///
    /// The wave periods are deliberately not multiples of each other, so the panorama does not
    /// repeat and the view comes back round in exactly one place.
    /// </summary>
    private static double[] Scenery(int length, double detail, int seed, int broadness)
    {
        Random random = new(seed);
        double[] raw = new double[length];
        for (int i = 0; i < length; i++)
        {
            raw[i] = random.NextDouble();
        }

        // Smoothing random detail with a wide window is what gives broad shapes without the
        // panorama ever repeating itself. Building it out of a few sine waves instead would look
        // right locally but make the far side of the turn resemble the near side, and the
        // measurement would honestly, and wrongly, find its way "back" after half a circle.
        double[] broad = Smooth(Smooth(raw, broadness), broadness);
        double[] fine = Smooth(raw, 2);

        double[] smoothed = new double[length];
        for (int i = 0; i < length; i++)
        {
            // Mostly broad shape, a little fine texture - the proportions of a real scene.
            smoothed[i] = (broad[i] * 1.0) + (fine[i] * 0.25);
        }

        double mean = smoothed.Average();
        double spread = Math.Sqrt(smoothed.Sum(v => (v - mean) * (v - mean)) / length);
        if (spread < 1e-9)
        {
            spread = 1;
        }

        double[] output = new double[length];
        for (int i = 0; i < length; i++)
        {
            output[i] = Math.Clamp(128 + ((smoothed[i] - mean) / spread * detail), 0, 255);
        }

        return output;
    }

    /// <summary>A box blur that wraps round, so the panorama has no seam.</summary>
    private static double[] Smooth(double[] values, int window)
    {
        int radius = Math.Max(1, window);
        double[] output = new double[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            double sum = 0;
            for (int k = -radius; k <= radius; k++)
            {
                sum += values[((i + k) % values.Length + values.Length) % values.Length];
            }

            output[i] = sum / ((radius * 2) + 1);
        }

        return output;
    }

    /// <summary>Runs a horizontal measurement to completion against this game.</summary>
    public AutoSpinSession RunSpin(AutoMeasureOptions? options = null)
    {
        AutoSpinSession session = new(options);
        session.Begin(ColumnProfile());

        while (true)
        {
            AutoStep? step = session.NextStep();
            if (step is null)
            {
                return session;
            }

            Move(step);
            session.Observe(ColumnProfile());
        }
    }

    /// <summary>Runs a vertical measurement to completion against this game.</summary>
    public AutoVerticalSession RunVertical(double pixelsPerCount, int rounds = 3, AutoMeasureOptions? options = null)
    {
        AutoVerticalSession session = new(pixelsPerCount, rounds, options);
        session.Begin(ColumnProfile(), RowProfile());

        while (true)
        {
            AutoStep? step = session.NextStep();
            if (step is null)
            {
                return session;
            }

            Move(step);
            session.Observe(ColumnProfile(), RowProfile());
        }
    }
}
