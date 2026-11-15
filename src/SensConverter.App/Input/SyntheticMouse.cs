using System.Runtime.InteropServices;

namespace SensConverter.App.Input;

/// <summary>
/// Moves the mouse pointer, for automatic measurement only.
///
/// This is the one part of the program that does not merely listen, and it is the reason automatic
/// measurement carries a warning. Anti-cheat software is built to notice mouse movement that no
/// hand produced, because that is what aim-assist cheats look like. It is fine in a single-player
/// game. In an online game with kernel-level anti-cheat it is a real risk, so nothing here runs
/// until the user has been shown that warning and accepted it.
///
/// Movement is broken into small pieces with a gap between them, which is both closer to what a
/// real mouse sends and kinder to games that limit how far the view may turn in one frame.
/// </summary>
internal static partial class SyntheticMouse
{
    /// <summary>Largest movement sent in one go. A fast flick on a real mouse is around this much.</summary>
    public const int MaxCountsPerBurst = 96;

    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;

    /// <summary>
    /// Sends a relative movement, split into bursts. Returns false when Windows refused to deliver
    /// it, which happens if a window running as administrator is in front.
    /// </summary>
    public static bool Move(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return true;
        }

        int steps = Math.Max(
            (Math.Abs(dx) + MaxCountsPerBurst - 1) / MaxCountsPerBurst,
            (Math.Abs(dy) + MaxCountsPerBurst - 1) / MaxCountsPerBurst);
        steps = Math.Max(1, steps);

        int sentX = 0;
        int sentY = 0;

        for (int i = 1; i <= steps; i++)
        {
            // Worked out from the running total rather than by repeating a rounded share, so the
            // pieces always add up to exactly the movement asked for.
            int wantX = (int)Math.Round((double)dx * i / steps);
            int wantY = (int)Math.Round((double)dy * i / steps);
            int burstX = wantX - sentX;
            int burstY = wantY - sentY;
            sentX = wantX;
            sentY = wantY;

            if (burstX == 0 && burstY == 0)
            {
                continue;
            }

            if (!SendOne(burstX, burstY))
            {
                return false;
            }

            if (steps > 1)
            {
                // Roughly the gap between reports from a fast gaming mouse.
                Thread.Sleep(1);
            }
        }

        return true;
    }

    private static bool SendOne(int dx, int dy)
    {
        MouseInput[] inputs =
        {
            new()
            {
                Type = InputMouse,
                Data = new MouseData
                {
                    Dx = dx,
                    Dy = dy,
                    WheelOrButton = 0,
                    Flags = MouseEventMove,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero,
                },
            },
        };

        return SendInput(1, inputs, Marshal.SizeOf<MouseInput>()) == 1;
    }

    /// <summary>
    /// Whether Windows is applying pointer acceleration ("Enhance pointer precision").
    ///
    /// Worth checking before measuring. Games that read the mouse directly are unaffected, but
    /// games that follow the pointer are not, and in those the measurement would come out wrong in
    /// a way nothing later could detect.
    /// </summary>
    public static bool IsPointerAccelerationOn()
    {
        int[] values = new int[3];

        if (!SystemParametersInfo(SpiGetMouse, 0, values, 0))
        {
            return false;
        }

        // The third value is the acceleration level: zero means off.
        return values[2] != 0;
    }

    private const uint SpiGetMouse = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public uint Type;
        public MouseData Data;
    }

    /// <summary>
    /// The mouse arm of the INPUT union. The union is as wide as its largest member (the keyboard
    /// one is smaller, the hardware one smaller again), and on 64-bit the trailing pointer makes
    /// the whole thing 32 bytes with the default packing, which is what SendInput expects.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public int Dx;
        public int Dy;
        public uint WheelOrButton;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, [In] MouseInput[] inputs, int size);

    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, [Out] int[] values, uint update);
}
