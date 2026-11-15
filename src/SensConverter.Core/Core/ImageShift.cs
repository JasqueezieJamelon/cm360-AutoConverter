namespace SensConverter.Core;

/// <summary>
/// How far one profile slid relative to another.
///
/// <see cref="Shift"/> is in samples (pixels of the captured frame) and says where the content of
/// the reference ended up: positive means it moved towards +x (right, or down for a row profile).
/// Turning the view to the right makes the world slide left, so a rightward turn gives a negative
/// shift.
/// </summary>
/// <param name="Shift">Offset in pixels, refined below whole pixels.</param>
/// <param name="Score">Match quality at that offset, from -1 to 1. 1 is a perfect match.</param>
/// <param name="Confidence">
/// How much better the winning offset is than the best *other* peak - not counting the winner's
/// own slope, which on a smooth picture reaches a long way. Low confidence means the picture has
/// repeating detail - a fence, a tiled floor - and the match could be off by a whole period, so the
/// measurement should not be trusted.
/// </param>
public sealed record ShiftMatch(double Shift, double Score, double Confidence)
{
    /// <summary>Below this the two profiles simply do not look like each other.</summary>
    public const double MinScore = 0.55;

    /// <summary>Below this the winning offset has a plausible rival, so it may be the wrong one.</summary>
    public const double MinConfidence = 0.10;

    public static ShiftMatch Failed { get; } = new(0, 0, 0);

    public bool IsReliable => Score >= MinScore && Confidence >= MinConfidence;
}

/// <summary>
/// Finds how far a picture moved, by sliding one brightness profile over another and seeing which
/// offset lines up best (a normalised cross-correlation).
///
/// This is the one piece of automatic measurement that has to be right, and it is also the one
/// piece that can be tested without a game: feed it a profile and a deliberately shifted copy, and
/// it has to report the shift back.
/// </summary>
public static class ImageShift
{
    /// <summary>Fewer overlapping samples than this and the score is meaningless.</summary>
    public const int MinOverlap = 32;

    /// <summary>
    /// Offsets this close to the winner are treated as part of the same peak when looking for a
    /// rival, so that the sample either side of the best one does not count against it.
    /// </summary>
    public const int PeakWidth = 3;

    /// <summary>
    /// Finds the offset that best lines <paramref name="moved"/> up with
    /// <paramref name="reference"/>, searching up to <paramref name="maxShift"/> samples each way.
    /// </summary>
    public static ShiftMatch Find(double[] reference, double[] moved, int maxShift)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(moved);

        if (reference.Length != moved.Length)
        {
            throw new ArgumentException("Profiles must be the same length.", nameof(moved));
        }

        int n = reference.Length;
        int limit = Math.Clamp(maxShift, 1, n - MinOverlap);
        if (n < MinOverlap * 2 || limit < 1)
        {
            return ShiftMatch.Failed;
        }

        int bestShift = 0;
        double bestScore = double.NegativeInfinity;
        double[] scores = new double[(limit * 2) + 1];

        for (int s = -limit; s <= limit; s++)
        {
            double score = ScoreAt(reference, moved, s);
            scores[s + limit] = score;

            if (score > bestScore)
            {
                bestScore = score;
                bestShift = s;
            }
        }

        if (double.IsNegativeInfinity(bestScore))
        {
            return ShiftMatch.Failed;
        }

        double confidence = Certainty(scores, bestShift + limit, bestScore);
        double refined = Refine(scores, bestShift, limit);

        return new ShiftMatch(refined, bestScore, confidence);
    }

    /// <summary>
    /// Normalised cross-correlation at one offset. Both sides have their average removed first, so
    /// the score measures shape alone - a scene that got brighter still matches itself.
    /// </summary>
    public static double ScoreAt(double[] reference, double[] moved, int shift)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(moved);

        int n = reference.Length;
        int from = Math.Max(0, -shift);
        int to = Math.Min(n - 1, n - 1 - shift);
        int count = to - from + 1;

        if (count < MinOverlap)
        {
            return double.NegativeInfinity;
        }

        double sumA = 0;
        double sumB = 0;
        for (int i = from; i <= to; i++)
        {
            sumA += reference[i];
            sumB += moved[i + shift];
        }

        double meanA = sumA / count;
        double meanB = sumB / count;

        double covariance = 0;
        double varianceA = 0;
        double varianceB = 0;
        for (int i = from; i <= to; i++)
        {
            double a = reference[i] - meanA;
            double b = moved[i + shift] - meanB;
            covariance += a * b;
            varianceA += a * a;
            varianceB += b * b;
        }

        double denominator = Math.Sqrt(varianceA * varianceB);

        // A flat profile has no shape to match; say so rather than dividing by zero.
        return denominator < 1e-9 ? 0 : covariance / denominator;
    }

    /// <summary>
    /// How sure the winning offset is, from 0 to 1.
    ///
    /// The thing worth catching is a picture whose detail repeats - a fence, a tiled floor, a row
    /// of identical windows - where the true offset cannot be told from the true offset plus one
    /// whole period. That shows up as a second peak nearly as tall as the winner.
    ///
    /// What must not be mistaken for a rival is the winner's own slope. Real scenery is mostly
    /// broad shapes, so its match peak is wide: a photograph lined up against itself still scores
    /// over 0.9 a long way either side of the correct offset. Counting that as a rival makes every
    /// ordinary scene look untrustworthy, which is exactly what it used to do - and automatic
    /// measurement then failed on every game, because no step it tried was ever believed.
    ///
    /// So the peak is first measured at half its own height. Everything inside that width is the
    /// winner; only a peak outside it is a rival. The answer is scaled against how far the scores
    /// range overall, so it means the same thing on a high-contrast scene and a flat one.
    /// </summary>
    private static double Certainty(double[] scores, int bestIndex, double bestScore)
    {
        double floor = double.PositiveInfinity;
        foreach (double score in scores)
        {
            if (IsReal(score) && score < floor)
            {
                floor = score;
            }
        }

        double span = bestScore - floor;
        if (!IsReal(floor) || span <= 1e-9)
        {
            // Every offset scores the same, so there is nothing to choose between them.
            return 0;
        }

        double halfHeight = floor + (span * 0.5);

        int left = bestIndex;
        while (left > 0 && IsReal(scores[left - 1]) && scores[left - 1] > halfHeight)
        {
            left--;
        }

        int right = bestIndex;
        while (right < scores.Length - 1 && IsReal(scores[right + 1]) && scores[right + 1] > halfHeight)
        {
            right++;
        }

        // Never count the immediate neighbours, however narrow the peak measures.
        left = Math.Min(left, bestIndex - PeakWidth);
        right = Math.Max(right, bestIndex + PeakWidth);

        double rival = double.NegativeInfinity;
        for (int i = 0; i < scores.Length; i++)
        {
            if ((i >= left && i <= right) || !IsReal(scores[i]))
            {
                continue;
            }

            rival = Math.Max(rival, scores[i]);
        }

        // The peak filled the whole search: nothing else to be confused with.
        return double.IsNegativeInfinity(rival) ? 1.0 : Math.Clamp((bestScore - rival) / span, 0, 1);
    }

    private static bool IsReal(double value) => !double.IsInfinity(value) && !double.IsNaN(value);

    /// <summary>
    /// Sharpens a whole-sample winner into a fractional one by fitting a parabola through it and
    /// its two neighbours. Worth doing: a tenth of a pixel here is a tenth of a percent on the
    /// final sensitivity.
    /// </summary>
    private static double Refine(double[] scores, int bestShift, int limit)
    {
        int index = bestShift + limit;
        if (index <= 0 || index >= scores.Length - 1)
        {
            return bestShift;
        }

        double left = scores[index - 1];
        double middle = scores[index];
        double right = scores[index + 1];

        if (double.IsNegativeInfinity(left) || double.IsNegativeInfinity(right))
        {
            return bestShift;
        }

        double denominator = left - (2 * middle) + right;
        if (Math.Abs(denominator) < 1e-12)
        {
            return bestShift;
        }

        double offset = 0.5 * (left - right) / denominator;

        // A parabola fit should never move the answer by more than half a sample; if it wants to,
        // the peak is not parabola-shaped and the whole-sample answer is the safer one.
        return Math.Abs(offset) > 0.5 ? bestShift : bestShift + offset;
    }
}
