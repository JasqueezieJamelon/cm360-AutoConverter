using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SensConverter.Core;

namespace SensConverter.App.Input;

/// <summary>
/// Takes a small grey picture of whatever game is in front.
///
/// Only a thumbnail is needed - a few hundred pixels across is plenty to tell how far the view
/// turned, and shrinking the picture both speeds up the matching and averages away the noise that
/// film grain and compression leave behind.
///
/// Only the middle of the window is captured. Game HUDs live around the edges and do not move when
/// the view turns, so leaving them out of the picture stops them dragging the match towards
/// "nothing moved".
///
/// This reads the screen the same way the Snipping Tool does. It does not read the game's memory
/// and does not attach to the game in any way.
/// </summary>
internal static partial class ScreenCapture
{
    /// <summary>How wide the shrunk picture is. Enough detail to match on, small enough to be quick.</summary>
    public const int TargetWidth = 480;

    /// <summary>The share of the window's width that is captured, centred.</summary>
    public const double RegionWidthFraction = 0.60;

    /// <summary>The share of its height.</summary>
    public const double RegionHeightFraction = 0.50;

    /// <summary>
    /// Which window is in front. Used to check the game has focus before measuring - with this
    /// program's own window in front, the movement would go to it and the capture would show it.
    /// </summary>
    public static IntPtr ForegroundWindow() => GetForegroundWindow();

    /// <summary>
    /// Grabs the middle of the foreground window. Returns null when there is nothing usable to
    /// capture, with <paramref name="error"/> saying why in words for the user.
    /// </summary>
    public static GrayFrame? CaptureForeground(out string error)
    {
        error = string.Empty;

        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            error = "No window is in the foreground.";
            return null;
        }

        if (!GetClientRect(window, out Rect client) || client.Right <= 0 || client.Bottom <= 0)
        {
            error = "Could not work out the size of the game window.";
            return null;
        }

        Point topLeft = new() { X = 0, Y = 0 };
        if (!ClientToScreen(window, ref topLeft))
        {
            error = "Could not work out where the game window is on screen.";
            return null;
        }

        int fullWidth = client.Right;
        int fullHeight = client.Bottom;
        int regionWidth = Math.Max(64, (int)(fullWidth * RegionWidthFraction));
        int regionHeight = Math.Max(64, (int)(fullHeight * RegionHeightFraction));
        int x = topLeft.X + ((fullWidth - regionWidth) / 2);
        int y = topLeft.Y + ((fullHeight - regionHeight) / 2);

        if (regionWidth < 128 || regionHeight < 72)
        {
            error = "The game window is too small to measure from. Make it bigger and try again.";
            return null;
        }

        try
        {
            return Grab(x, y, regionWidth, regionHeight, out error);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException or ArgumentException)
        {
            error = "The screen could not be captured: " + ex.Message;
            return null;
        }
    }

    /// <summary>Copies a rectangle of the screen and shrinks it to a grey thumbnail.</summary>
    private static GrayFrame? Grab(int x, int y, int width, int height, out string error)
    {
        error = string.Empty;

        int targetWidth = Math.Min(TargetWidth, width);
        int targetHeight = Math.Max(ImageShift.MinOverlap * 2, (int)Math.Round(height * (targetWidth / (double)width)));

        using Bitmap full = new(width, height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(full))
        {
            graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        using Bitmap small = new(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(small))
        {
            // Averaging while shrinking is what makes sub-pixel matching possible later.
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(full, new Rectangle(0, 0, targetWidth, targetHeight));
        }

        GrayFrame frame = ToGray(small);

        // A fullscreen game that bypasses the desktop compositor hands back a black rectangle
        // rather than a picture. Saying so is far better than silently measuring nothing.
        if (GrayFrame.Detail(frame.ColumnProfile(1.0)) < 0.5)
        {
            error = "The screen came back blank. If the game is running in exclusive fullscreen, "
                    + "switch it to borderless or windowed mode and try again.";
            return null;
        }

        return frame;
    }

    /// <summary>Flattens a bitmap to one brightness byte per pixel.</summary>
    private static GrayFrame ToGray(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        byte[] gray = new byte[width * height];

        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            unsafe
            {
                byte* scan = (byte*)data.Scan0;
                for (int row = 0; row < height; row++)
                {
                    byte* line = scan + (row * data.Stride);
                    int target = row * width;

                    for (int column = 0; column < width; column++)
                    {
                        byte blue = line[column * 4];
                        byte green = line[(column * 4) + 1];
                        byte red = line[(column * 4) + 2];

                        // The usual perceptual weighting, so a red wall and a green one do not
                        // come out as the same shade of grey.
                        gray[target + column] = (byte)(((red * 299) + (green * 587) + (blue * 114)) / 1000);
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return new GrayFrame(width, height, gray);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr window, out Rect rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(IntPtr window, ref Point point);
}
