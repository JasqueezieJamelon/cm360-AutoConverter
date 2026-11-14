using System.Diagnostics;

namespace SensConverter.Core;

/// <summary>What one recorded spin attempt looked like.</summary>
public sealed class SpinRecord
{
    /// <summary>A spin shorter than this is a slip of the hand, not a 360.</summary>
    public const double MinimumUsefulCounts = 100;

    /// <summary>Total travel more than this multiple of the net turn means a lot of back-and-forth.</summary>
    public const double WobbleWarnRatio = 1.25;

    /// <summary>Signed horizontal counts: left and right cancel out, so overshoot-and-correct is handled.</summary>
    public required double NetCounts { get; init; }

    /// <summary>Every count of horizontal movement regardless of direction.</summary>
    public required double TravelCounts { get; init; }

    /// <summary>Vertical counts, only used to point out a very tilted swipe.</summary>
    public required double VerticalCounts { get; init; }

    public required TimeSpan Duration { get; init; }

    /// <summary>The number calibration actually uses.</summary>
    public double Counts => Math.Abs(NetCounts);

    /// <summary>1.0 is a perfectly clean single sweep; 2.0 means twice as much hand movement as net turn.</summary>
    public double WobbleRatio => Counts > 0 ? TravelCounts / Counts : 0;

    public bool IsUsable => Counts >= MinimumUsefulCounts;

    /// <summary>A note for the user, or null when the spin looks clean.</summary>
    public string? Warning
    {
        get
        {
            if (!IsUsable)
            {
                return "Barely moved - was the spin recorded by mistake?";
            }

            if (WobbleRatio > WobbleWarnRatio)
            {
                return "Lots of back-and-forth; a single smooth sweep is more accurate.";
            }

            if (Counts > 0 && Math.Abs(VerticalCounts) > Counts * 0.5)
            {
                return "Very tilted swipe - try to keep it horizontal.";
            }

            return null;
        }
    }
}

/// <summary>
/// Counts mouse movement during one spin. Fed raw mouse deltas; knows nothing about how they arrive.
/// </summary>
public sealed class SpinRecorder
{
    private readonly Stopwatch stopwatch = new();

    public bool IsRecording { get; private set; }

    public double NetCounts { get; private set; }

    public double TravelCounts { get; private set; }

    public double VerticalCounts { get; private set; }

    public TimeSpan Elapsed => stopwatch.Elapsed;

    /// <summary>Live reading while the spin is in progress.</summary>
    public double CurrentCounts => Math.Abs(NetCounts);

    public void Start()
    {
        Reset();
        IsRecording = true;
        stopwatch.Restart();
    }

    public void Add(int dx, int dy)
    {
        if (!IsRecording)
        {
            return;
        }

        NetCounts += dx;
        TravelCounts += Math.Abs(dx);
        VerticalCounts += dy;
    }

    public SpinRecord Stop()
    {
        stopwatch.Stop();
        IsRecording = false;

        return new SpinRecord
        {
            NetCounts = NetCounts,
            TravelCounts = TravelCounts,
            VerticalCounts = VerticalCounts,
            Duration = stopwatch.Elapsed,
        };
    }

    public void Reset()
    {
        IsRecording = false;
        NetCounts = 0;
        TravelCounts = 0;
        VerticalCounts = 0;
        stopwatch.Reset();
    }
}
