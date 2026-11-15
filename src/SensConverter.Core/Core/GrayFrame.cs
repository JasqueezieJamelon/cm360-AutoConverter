namespace SensConverter.Core;

/// <summary>
/// One captured frame, reduced to grey.
///
/// Automatic measurement never needs colour, and never needs full resolution - it only needs to
/// know how far the picture slid sideways. Reducing a screenshot to grey here keeps the matching
/// code small and makes it testable without any screen at all.
///
/// A frame is turned into a one-dimensional "profile" before matching: the average brightness of
/// each column (for horizontal movement) or of each row (for vertical). Matching two profiles is
/// far cheaper than matching two images, and for a camera that is panning it works just as well,
/// because every column moves by the same amount.
/// </summary>
public sealed class GrayFrame
{
    public GrayFrame(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentNullException.ThrowIfNull(pixels);

        if (pixels.Length < (long)width * height)
        {
            throw new ArgumentException(
                $"Expected at least {(long)width * height} pixels for {width}x{height}, got {pixels.Length}.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Row-major, one byte per pixel.</summary>
    public byte[] Pixels { get; }

    /// <summary>
    /// Average brightness per column, over a horizontal band of the frame.
    ///
    /// The band matters. Game HUDs live at the top and bottom of the screen and do not move when
    /// the view turns, so including them drags the match towards "nothing moved". Taking a band
    /// around the middle - by default the middle half - keeps mostly world.
    /// </summary>
    public double[] ColumnProfile(double bandFraction = 0.5)
    {
        (int first, int last) = Band(Height, bandFraction);
        double[] profile = new double[Width];
        int rows = last - first + 1;

        for (int y = first; y <= last; y++)
        {
            int rowStart = y * Width;
            for (int x = 0; x < Width; x++)
            {
                profile[x] += Pixels[rowStart + x];
            }
        }

        for (int x = 0; x < Width; x++)
        {
            profile[x] /= rows;
        }

        return profile;
    }

    /// <summary>Average brightness per row, over a vertical band. The vertical counterpart of
    /// <see cref="ColumnProfile"/>; the band here trims the left and right edges.</summary>
    public double[] RowProfile(double bandFraction = 0.5)
    {
        (int first, int last) = Band(Width, bandFraction);
        double[] profile = new double[Height];
        int columns = last - first + 1;

        for (int y = 0; y < Height; y++)
        {
            int rowStart = y * Width;
            double sum = 0;
            for (int x = first; x <= last; x++)
            {
                sum += Pixels[rowStart + x];
            }

            profile[y] = sum / columns;
        }

        return profile;
    }

    /// <summary>
    /// How much detail the frame has to match on, as a standard deviation in brightness. A flat
    /// grey wall or a loading screen scores near zero, and there is no way to measure movement
    /// against it - the caller should ask the user to look at something with more in it.
    /// </summary>
    public static double Detail(double[] profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Length < 2)
        {
            return 0;
        }

        double mean = profile.Average();
        double sumSquares = profile.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSquares / profile.Length);
    }

    /// <summary>A centred band covering <paramref name="fraction"/> of the given length.</summary>
    private static (int First, int Last) Band(int length, double fraction)
    {
        double clamped = Math.Clamp(double.IsNaN(fraction) ? 0.5 : fraction, 0.05, 1.0);
        int span = Math.Max(1, (int)Math.Round(length * clamped));
        int first = Math.Clamp((length - span) / 2, 0, length - 1);
        int last = Math.Clamp(first + span - 1, first, length - 1);
        return (first, last);
    }
}
