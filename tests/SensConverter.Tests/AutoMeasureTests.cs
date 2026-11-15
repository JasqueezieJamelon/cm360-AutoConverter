using SensConverter.Core;

namespace SensConverter.Tests;

internal static class AutoMeasureTests
{
    public static void Register(Harness h)
    {
        ArgumentNullException.ThrowIfNull(h);

        RegisterGrayFrame(h);
        RegisterImageShift(h);
        RegisterSpin(h);
        RegisterVertical(h);
    }

    // ---------------- GrayFrame ----------------

    private static void RegisterGrayFrame(Harness h)
    {
        h.Section("GrayFrame");

        h.Test("a column profile averages down each column", () =>
        {
            // Three columns, four rows; each column a constant value.
            byte[] pixels =
            {
                10, 20, 30,
                10, 20, 30,
                10, 20, 30,
                10, 20, 30,
            };
            GrayFrame frame = new(3, 4, pixels);
            double[] profile = frame.ColumnProfile(1.0);

            Harness.AreEqual(3, profile.Length, "one value per column");
            Harness.AreClose(10, profile[0], "first column");
            Harness.AreClose(20, profile[1], "second column");
            Harness.AreClose(30, profile[2], "third column");
        });

        h.Test("a row profile averages across each row", () =>
        {
            byte[] pixels =
            {
                10, 10, 10,
                20, 20, 20,
                30, 30, 30,
                40, 40, 40,
            };
            GrayFrame frame = new(3, 4, pixels);
            double[] profile = frame.RowProfile(1.0);

            Harness.AreEqual(4, profile.Length, "one value per row");
            Harness.AreClose(10, profile[0], "first row");
            Harness.AreClose(40, profile[3], "last row");
        });

        h.Test("the middle band ignores a HUD at the top and bottom", () =>
        {
            // Rows 0 and 3 are a bright HUD; rows 1 and 2 are the world.
            byte[] pixels =
            {
                255, 255,
                40, 80,
                40, 80,
                255, 255,
            };
            GrayFrame frame = new(2, 4, pixels);

            double[] whole = frame.ColumnProfile(1.0);
            double[] middle = frame.ColumnProfile(0.5);

            Harness.AreClose(40, middle[0], "HUD excluded from the first column");
            Harness.AreClose(80, middle[1], "HUD excluded from the second column");
            Harness.IsTrue(whole[0] > middle[0], "including the HUD does change the answer");
        });

        h.Test("detail is zero for a flat picture and positive for a varied one", () =>
        {
            Harness.AreClose(0, GrayFrame.Detail(new[] { 50.0, 50, 50, 50 }), "flat");
            Harness.IsTrue(GrayFrame.Detail(new[] { 10.0, 200, 30, 170 }) > 10, "varied");
        });

        h.Test("a frame smaller than its pixel count is refused", () =>
            Harness.Throws<ArgumentException>(() => new GrayFrame(10, 10, new byte[5]), "too few pixels"));
    }

    // ---------------- ImageShift ----------------

    private static void RegisterImageShift(Harness h)
    {
        h.Section("ImageShift");

        h.Test("an unmoved picture reports no shift", () =>
        {
            double[] profile = Scenery(300, seed: 1);
            ShiftMatch match = ImageShift.Find(profile, profile, 40);

            Harness.IsTrue(match.IsReliable, "identical profiles match");
            Harness.AreClose(0, match.Shift, "no shift", 0.01);
            Harness.AreClose(1, match.Score, "perfect score", 1e-6);
        });

        h.Test("a whole-pixel shift is found exactly", () =>
        {
            double[] profile = Scenery(400, seed: 2);

            foreach (int expected in new[] { -37, -8, -1, 5, 19, 50 })
            {
                double[] moved = Shift(profile, expected);
                ShiftMatch match = ImageShift.Find(profile, moved, 80);

                Harness.IsTrue(match.IsReliable, $"shift of {expected} is reliable");
                Harness.AreClose(expected, match.Shift, $"shift of {expected}", 0.05);
            }
        });

        h.Test("a part-pixel shift is found to a fraction of a pixel", () =>
        {
            double[] profile = Scenery(400, seed: 3);

            foreach (double expected in new[] { 6.5, -11.25, 23.4 })
            {
                double[] moved = Shift(profile, expected);
                ShiftMatch match = ImageShift.Find(profile, moved, 80);

                Harness.IsTrue(match.IsReliable, $"shift of {expected} is reliable");
                Harness.AreClose(expected, match.Shift, $"shift of {expected}", 0.2);
            }
        });

        h.Test("a scene that got brighter still matches itself", () =>
        {
            double[] profile = Scenery(400, seed: 4);
            double[] moved = Shift(profile, 15).Select(v => (v * 1.3) + 25).ToArray();

            ShiftMatch match = ImageShift.Find(profile, moved, 60);
            Harness.IsTrue(match.IsReliable, "brightness change tolerated");
            Harness.AreClose(15, match.Shift, "shift despite brightness change", 0.2);
        });

        h.Test("a blank picture is reported as unreliable, not as a shift of zero", () =>
        {
            double[] flat = new double[300];
            Array.Fill(flat, 128.0);

            ShiftMatch match = ImageShift.Find(flat, flat, 40);
            Harness.IsFalse(match.IsReliable, "nothing to match on");
        });

        h.Test("repeating detail is flagged as low confidence", () =>
        {
            // A picket fence: every 20 pixels looks like every other 20 pixels, so the true shift
            // cannot be told from the true shift plus 20.
            double[] fence = new double[400];
            for (int i = 0; i < fence.Length; i++)
            {
                fence[i] = 128 + (40 * Math.Sin(i * 2 * Math.PI / 20));
            }

            double[] moved = Shift(fence, 7);
            ShiftMatch match = ImageShift.Find(fence, moved, 80);

            Harness.IsTrue(match.Score > 0.9, "it does find a strong match somewhere");
            Harness.IsTrue(match.Confidence < ShiftMatch.MinConfidence, "but an ambiguous one");
            Harness.IsFalse(match.IsReliable, "so it is not trusted");
        });

        h.Test("a smooth, real-looking picture is trusted", () =>
        {
            // The one that got away. Real scenery is mostly broad shapes, not fine noise, so its
            // match peak is wide - a photograph lined up against itself still scores over 0.9 well
            // away from the correct offset. Counting that slope as a rival made every match of
            // every ordinary scene look unreliable, and automatic measurement failed on every game
            // with "could not find a mouse movement this game responds to".
            double[] smooth = SmoothScenery(480, seed: 11);

            ShiftMatch still = ImageShift.Find(smooth, smooth, 240);
            Harness.IsTrue(still.Score > 0.99, $"identical frames score high, got {still.Score:F3}");
            Harness.IsTrue(
                still.IsReliable,
                $"identical frames must be trusted (score {still.Score:F3}, confidence {still.Confidence:F3})");

            foreach (double by in new[] { -40.0, -12.5, 7.0, 33.0 })
            {
                ShiftMatch moved = ImageShift.Find(smooth, Shift(smooth, by), 240);
                Harness.IsTrue(
                    moved.IsReliable,
                    $"a shift of {by} must be trusted (score {moved.Score:F3}, confidence {moved.Confidence:F3})");
                Harness.AreClose(by, moved.Shift, $"shift of {by} on smooth scenery", 0.6);
            }
        });

        h.Test("a gentle gradient across the whole frame is still trusted", () =>
        {
            // The hardest honest case: almost no detail beyond one broad ramp, which is what a
            // wall lit from one side looks like. The peak then covers the entire search range.
            double[] ramp = new double[400];
            for (int i = 0; i < ramp.Length; i++)
            {
                ramp[i] = 60 + (i * 0.35) + (12 * Math.Sin(i / 90.0));
            }

            ShiftMatch match = ImageShift.Find(ramp, Shift(ramp, 15), 80);
            Harness.IsTrue(match.IsReliable, $"confidence {match.Confidence:F3}, score {match.Score:F3}");
            Harness.AreClose(15, match.Shift, "shift on a gradient", 1.0);
        });

        h.Test("mismatched profile lengths are refused", () =>
            Harness.Throws<ArgumentException>(
                () => ImageShift.Find(new double[100], new double[120], 10),
                "different lengths"));
    }

    // ---------------- the horizontal 360 ----------------

    private static void RegisterSpin(Harness h)
    {
        h.Section("AutoSpinSession");

        h.Test("it finds the counts for a 360 on a normal game", () =>
        {
            FakeGame game = new(counts360: 9449);
            AutoSpinSession session = game.RunSpin();

            Harness.AreEqual(AutoPhase.Done, session.Phase, session.Failure ?? "finished");
            Harness.IsTrue(
                Within(9449, session.Counts360, 0.01),
                $"expected about 9449 counts, got {session.Counts360:F0}");
        });

        h.Test("it works across the whole range of game sensitivities", () =>
        {
            // From a twitchy game where a 360 is a flick, to one that needs a whole desk.
            foreach (double counts360 in new[] { 400.0, 1200, 3000, 9449, 22000, 60000 })
            {
                FakeGame game = new(counts360);
                AutoSpinSession session = game.RunSpin();

                Harness.AreEqual(AutoPhase.Done, session.Phase, session.Failure ?? $"finished at {counts360}");
                Harness.IsTrue(
                    Within(counts360, session.Counts360, 0.02),
                    $"expected about {counts360:F0} counts, got {session.Counts360:F0}");
            }
        });

        h.Test("the answer is far better than the step size it was found with", () =>
        {
            // The leftover correction is the whole point: without it the answer could only ever be
            // as good as one sweeping step, which is a fifth of a screen width.
            FakeGame game = new(counts360: 9449);
            AutoSpinSession session = game.RunSpin();

            double stepCounts = 0.2 * 480 / game.PixelsPerCount;
            double error = Math.Abs(session.Counts360 - 9449);

            Harness.IsTrue(
                error < stepCounts / 4,
                $"error {error:F0} counts should be well under a quarter of a step ({stepCounts / 4:F0})");
        });

        h.Test("it finds how far the picture moves per count", () =>
        {
            FakeGame game = new(counts360: 9449);
            AutoSpinSession session = game.RunSpin();

            Harness.IsTrue(
                Within(game.PixelsPerCount, session.PixelsPerCount, 0.1),
                $"expected about {game.PixelsPerCount:F4} px per count, got {session.PixelsPerCount:F4}");
        });

        h.Test("it works at any field of view a game might use", () =>
        {
            // A narrow field of view spreads a turn over more screens of scenery, a wide one over
            // fewer. The sanity check that rejects an impossible answer has to leave room for both
            // ends of what a real game offers.
            foreach (double widths in new[] { 2.0, 3.3, 5.2 })
            {
                FakeGame game = new(counts360: 9449, panoramaWidths: widths);
                AutoSpinSession session = game.RunSpin();

                Harness.AreEqual(AutoPhase.Done, session.Phase, session.Failure ?? $"finished at {widths} widths");
                Harness.IsTrue(
                    Within(9449, session.Counts360, 0.02),
                    $"at {widths} screen widths per turn, expected about 9449, got {session.Counts360:F0}");
            }
        });

        h.Test("a blank screen is refused with something the user can act on", () =>
        {
            FakeGame game = new(counts360: 9449, detail: 0.0);
            AutoSpinSession session = game.RunSpin();

            Harness.AreEqual(AutoPhase.Failed, session.Phase, "cannot measure a blank screen");
            Harness.IsTrue(session.Failure is not null && session.Failure.Contains("detail"), "says what to do");
        });

        h.Test("in-game mouse acceleration is noticed", () =>
        {
            FakeGame game = new(counts360: 9449, accelerationExponent: 1.08);
            AutoSpinSession session = game.RunSpin();

            // It may or may not reach an answer, but either way it must not claim the game is linear.
            Harness.IsTrue(
                session.Phase == AutoPhase.Failed || session.LooksNonLinear,
                "acceleration is either flagged or reported as a failure");
        });

        h.Test("a well-behaved game is not accused of acceleration", () =>
        {
            FakeGame game = new(counts360: 9449);
            AutoSpinSession session = game.RunSpin();

            Harness.IsFalse(session.LooksNonLinear, "no false alarm on a linear game");
        });

        h.Test("progress goes up and finishes at 1", () =>
        {
            FakeGame game = new(counts360: 9449);
            AutoSpinSession session = new();
            session.Begin(game.ColumnProfile());

            double previous = -1;
            while (true)
            {
                AutoStep? step = session.NextStep();
                if (step is null)
                {
                    break;
                }

                game.Move(step);
                session.Observe(game.ColumnProfile());

                Harness.IsTrue(session.Progress >= previous - 1e-9, "progress never goes backwards");
                previous = session.Progress;
            }

            Harness.AreEqual(AutoPhase.Done, session.Phase, session.Failure ?? "finished");
            Harness.AreClose(1, session.Progress, "finished progress");
        });

        h.Test("it gives up when the view never comes back round", () =>
        {
            // A world far too wide to be a 360: the starting view never reappears, which is what a
            // game that drifts, or that ignores the mouse part of the time, would look like.
            FakeGame game = new(counts360: 9449, panoramaWidths: 40);
            AutoSpinSession session = game.RunSpin();

            Harness.AreEqual(AutoPhase.Failed, session.Phase, "does not turn forever");
            Harness.IsTrue(
                session.Failure is not null && session.Failure.Contains("past a full circle"),
                $"says why: {session.Failure}");
        });

        h.Test("it gives up when the game does not respond to the mouse at all", () =>
        {
            // Every frame is unrelated to the last, as if the game were paused or not listening.
            AutoSpinSession session = new();
            session.Begin(Scenery(480, seed: 7));

            int frame = 0;
            while (true)
            {
                AutoStep? step = session.NextStep();
                if (step is null)
                {
                    break;
                }

                session.Observe(Scenery(480, seed: 100 + frame++));
            }

            Harness.AreEqual(AutoPhase.Failed, session.Phase, "does not try forever");
            Harness.IsTrue(
                session.Failure is not null && session.Failure.Contains("responds to"),
                $"says why: {session.Failure}");
        });

        h.Test("the step cap is honoured", () =>
        {
            FakeGame game = new(counts360: 9449, panoramaWidths: 40);
            AutoSpinSession session = game.RunSpin(new AutoMeasureOptions { MaxSteps = 12 });

            Harness.AreEqual(AutoPhase.Failed, session.Phase, "stops at the cap");
            Harness.IsTrue(session.Steps <= 12, $"took {session.Steps} steps, cap was 12");
        });

        h.Test("the measured counts convert into the right sensitivity", () =>
        {
            // The whole point of the exercise, end to end: measure a game, then work out the sens.
            FakeGame game = new(counts360: 9449);
            AutoSpinSession session = game.RunSpin();

            double coefficient = SensMath.CoefficientFromCalibration(session.Counts360, 1.0);
            double sens = SensMath.SensForCm360(coefficient, 30, 1600);

            Harness.IsTrue(Within(0.5, sens, 0.02), $"expected a sens near 0.50, got {sens:F3}");
            Harness.AreClose(0.5, SensMath.RoundSens(sens), "rounds to the clean answer", 1e-9);
        });
    }

    // ---------------- the vertical axis ----------------

    private static void RegisterVertical(Harness h)
    {
        h.Section("AutoVerticalSession");

        h.Test("a game that treats both axes alike measures as 1.0", () =>
        {
            FakeGame game = new(counts360: 9449, verticalRatio: 1.0);
            AutoVerticalSession session = game.RunVertical(game.PixelsPerCount);

            Harness.AreEqual(AutoPhase.Done, session.Phase, session.Failure ?? "finished");
            Harness.IsTrue(Within(1.0, session.VerticalRatio, 0.05), $"got {session.VerticalRatio:F3}");
        });

        h.Test("a game with a slower vertical axis measures below 1.0", () =>
        {
            foreach (double ratio in new[] { 0.5, 0.75, 1.4 })
            {
                FakeGame game = new(counts360: 9449, verticalRatio: ratio);
                AutoVerticalSession session = game.RunVertical(game.PixelsPerCount);

                Harness.AreEqual(AutoPhase.Done, session.Phase, session.Failure ?? $"finished at {ratio}");
                Harness.IsTrue(
                    Within(ratio, session.VerticalRatio, 0.06),
                    $"expected about {ratio:F2}, got {session.VerticalRatio:F3}");
            }
        });

        h.Test("hitting the look-up limit is caught instead of being measured wrongly", () =>
        {
            // The view can barely pitch at all, so looking down moves and looking up does not.
            FakeGame game = new(counts360: 9449, pitchLimitPixels: 4);
            AutoVerticalSession session = game.RunVertical(game.PixelsPerCount);

            Harness.AreEqual(AutoPhase.Failed, session.Phase, "clamped pitch is refused");
            Harness.IsTrue(session.Failure is not null && session.Failure.Contains("limit"), "says what happened");
        });

        h.Test("a blank screen is refused", () =>
        {
            FakeGame game = new(counts360: 9449, detail: 0.0);
            AutoVerticalSession session = game.RunVertical(game.PixelsPerCount);

            Harness.AreEqual(AutoPhase.Failed, session.Phase, "cannot measure a blank screen");
        });

        h.Test("the ratio turns into a vertical sens that matches the wanted feel", () =>
        {
            // A game whose vertical axis is half as sensitive needs twice the vertical sens to
            // feel the same as its horizontal one.
            FakeGame game = new(counts360: 9449, verticalRatio: 0.5);
            AutoSpinSession spin = game.RunSpin();
            AutoVerticalSession vertical = game.RunVertical(spin.PixelsPerCount);

            AppSettings settings = new() { Dpi = 1600, TargetCm360 = 30, VerticalMultiplier = 1.0 };
            GameEntry entry = GameEntry.FromCalibration(
                "Fake", string.Empty, string.Empty, 1.0, new[] { spin.Counts360 });

            double degreesPerCountX = 360.0 / spin.Counts360;
            double degreesPerCountY = degreesPerCountX * vertical.VerticalRatio;
            entry.SetVertical(degreesPerCountY * 1000, 1000, 1.0);

            double sensX = entry.SensFor(settings);
            double sensY = entry.SensYFor(settings);

            Harness.IsTrue(Within(0.5, sensX, 0.03), $"horizontal sens {sensX:F3}");
            Harness.IsTrue(Within(1.0, sensY, 0.06), $"vertical sens {sensY:F3} should be about double");
        });

        h.Test("a sensible nudge size is rejected if it is nonsense", () =>
            Harness.Throws<ArgumentOutOfRangeException>(
                () => new AutoVerticalSession(0),
                "zero pixels per count"));
    }

    // ---------------- helpers ----------------

    /// <summary>True when <paramref name="actual"/> is within a fraction of <paramref name="expected"/>.</summary>
    private static bool Within(double expected, double actual, double fraction)
        => Math.Abs(actual - expected) <= Math.Abs(expected) * fraction;

    /// <summary>
    /// Scenery that behaves like a real picture rather than like noise: a few broad shapes with
    /// finer detail laid over them. The broad shapes are the point - they are what makes a real
    /// match peak wide, and a test built only on fine noise never exercises that.
    /// </summary>
    private static double[] SmoothScenery(int length, int seed)
    {
        Random random = new(seed);
        double[] profile = new double[length];

        // A handful of slow waves at random phases: hills, walls, large blocks of light and shade.
        for (int wave = 0; wave < 6; wave++)
        {
            double period = length / (2.0 + (wave * 2.5));
            double phase = random.NextDouble() * Math.PI * 2;
            double amplitude = 40.0 / (1 + wave);

            for (int i = 0; i < length; i++)
            {
                profile[i] += amplitude * Math.Sin((i / period * Math.PI * 2) + phase);
            }
        }

        // A little fine texture on top, as any real surface has.
        for (int i = 0; i < length; i++)
        {
            profile[i] += (random.NextDouble() - 0.5) * 6;
            profile[i] = Math.Clamp(profile[i] + 128, 0, 255);
        }

        return profile;
    }

    /// <summary>A smoothed random profile, standing in for a picture with detail in it.</summary>
    private static double[] Scenery(int length, int seed)
    {
        Random random = new(seed);
        double[] raw = new double[length];
        for (int i = 0; i < length; i++)
        {
            raw[i] = random.NextDouble() * 255;
        }

        double[] smoothed = new double[length];
        for (int i = 0; i < length; i++)
        {
            double sum = 0;
            for (int k = -3; k <= 3; k++)
            {
                sum += raw[((i + k) % length + length) % length];
            }

            smoothed[i] = sum / 7;
        }

        return smoothed;
    }

    /// <summary>Slides a profile along by a possibly fractional amount, wrapping round.</summary>
    private static double[] Shift(double[] profile, double by)
    {
        double[] moved = new double[profile.Length];
        for (int i = 0; i < profile.Length; i++)
        {
            double source = i - by;
            int low = (int)Math.Floor(source);
            double fraction = source - low;
            int a = ((low % profile.Length) + profile.Length) % profile.Length;
            int b = ((low + 1) % profile.Length + profile.Length) % profile.Length;
            moved[i] = (profile[a] * (1 - fraction)) + (profile[b] * fraction);
        }

        return moved;
    }
}
