using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using SensConverter.Core;

namespace SensConverter.App.Input;

/// <summary>
/// A record of what a measurement actually saw, written out when one fails.
///
/// An automatic measurement fails in the dark: the program says it could not make the view move,
/// but it cannot say whether that is because the movement never reached the game, or because the
/// picture it was looking at was not the game at all. Those have completely different fixes and
/// look identical from the outside.
///
/// So every run keeps the frames it captured and what it measured between them. If the run fails,
/// they get written next to the settings as plain PNGs and a text file, and looking at the first
/// two images answers the question immediately.
/// </summary>
internal sealed class MeasurementDiagnostics
{
    /// <summary>How many frames from the start of a run are kept.</summary>
    public const int FirstFrames = 4;

    /// <summary>And how many from the end, where a run that stopped too early gives itself away.</summary>
    public const int LastFrames = 10;

    private readonly List<string> lines = new();
    private readonly List<(string Name, GrayFrame Frame)> opening = new();
    private readonly Queue<(string Name, GrayFrame Frame)> closing = new();
    private readonly DateTimeOffset started = DateTimeOffset.Now;

    public void Note(string line)
    {
        lines.Add($"[{(DateTimeOffset.Now - started).TotalMilliseconds,7:F0} ms] {line}");
    }

    /// <summary>
    /// Keeps a captured frame: the opening ones, and a rolling window of the most recent. The
    /// middle of a long turn says little, while the start and the finish are where a measurement
    /// that stopped in the wrong place shows it - the last frame should look like the first.
    /// </summary>
    public void Keep(string name, GrayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (opening.Count < FirstFrames)
        {
            opening.Add((name, frame));
            return;
        }

        closing.Enqueue((name, frame));
        while (closing.Count > LastFrames)
        {
            closing.Dequeue();
        }
    }

    /// <summary>
    /// Writes everything to a new folder and returns it, or null if it could not be written -
    /// which must never turn into a second error on top of the first.
    /// </summary>
    public string? Save(string parentDirectory)
    {
        try
        {
            string stamp = started.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string directory = Path.Combine(parentDirectory, "diagnostics", stamp);
            Directory.CreateDirectory(directory);

            StringBuilder text = new();
            text.AppendLine("Sensitivity Converter - automatic measurement diagnostics");
            text.AppendLine(started.ToString("u", CultureInfo.InvariantCulture));
            text.AppendLine();
            text.AppendLine("The numbered PNGs are what the program captured, in order. If they all look");
            text.AppendLine("the same, the view was not turning. If they do not show the game at all, the");
            text.AppendLine("capture is reading the wrong thing.");
            text.AppendLine();

            foreach (string line in lines)
            {
                text.AppendLine(line);
            }

            File.WriteAllText(Path.Combine(directory, "log.txt"), text.ToString());

            (string Name, GrayFrame Frame)[] all = opening.Concat(closing).ToArray();
            for (int i = 0; i < all.Length; i++)
            {
                string file = Path.Combine(
                    directory,
                    string.Create(CultureInfo.InvariantCulture, $"{i:00}-{Sanitise(all[i].Name)}.png"));
                SavePng(all[i].Frame, file);
            }

            return directory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string Sanitise(string name)
    {
        char[] cleaned = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        return new string(cleaned);
    }

    /// <summary>Turns the grey bytes back into a picture a person can look at.</summary>
    private static void SavePng(GrayFrame frame, string path)
    {
        using Bitmap bitmap = new(frame.Width, frame.Height, PixelFormat.Format24bppRgb);

        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, frame.Width, frame.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format24bppRgb);

        try
        {
            unsafe
            {
                byte* scan = (byte*)data.Scan0;
                for (int y = 0; y < frame.Height; y++)
                {
                    byte* line = scan + (y * data.Stride);
                    int source = y * frame.Width;

                    for (int x = 0; x < frame.Width; x++)
                    {
                        byte value = frame.Pixels[source + x];
                        line[(x * 3) + 0] = value;
                        line[(x * 3) + 1] = value;
                        line[(x * 3) + 2] = value;
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.Save(path, ImageFormat.Png);
    }
}
