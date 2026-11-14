using System.Runtime.InteropServices;
using SensConverter.Core;

namespace SensConverter.App.Input;

/// <summary>
/// A hidden window that listens, and only listens.
///
/// Two things arrive here: raw mouse movement (registered with RIDEV_INPUTSINK, so it keeps coming
/// while a game has focus) and one global hotkey. Nothing is ever injected or sent to the game, so
/// there is nothing for an anti-cheat to object to.
///
/// Both messages land on the thread that created this window - the UI thread - so event handlers can
/// touch controls directly.
/// </summary>
internal sealed partial class GlobalInput : NativeWindow, IDisposable
{
    private const int WmInput = 0x00FF;
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x5CA1;

    private const uint RidInput = 0x10000003;
    private const uint RimTypeMouse = 0;
    private const ushort UsagePageGeneric = 0x01;
    private const ushort UsageMouse = 0x02;
    private const uint RidevInputSink = 0x00000100;
    private const ushort MouseMoveAbsolute = 0x01;

    private IntPtr buffer;
    private int bufferSize;
    private bool hotkeyRegistered;
    private bool disposed;

    public GlobalInput()
    {
        // A hidden top-level window (not a message-only window): raw input sinks are reliable here.
        CreateHandle(new CreateParams
        {
            Caption = "SensConverterInputSink",
            X = 0,
            Y = 0,
            Width = 0,
            Height = 0,
            Style = 0,
            ExStyle = 0,
            Parent = IntPtr.Zero,
        });

        RegisterRawMouse();
    }

    /// <summary>Raw mouse movement in counts. Positive dx is right, positive dy is down.</summary>
    public event Action<int, int>? MouseMoved;

    /// <summary>The global hotkey was pressed.</summary>
    public event Action? HotkeyPressed;

    public bool RawInputAvailable { get; private set; }

    public Hotkey? CurrentHotkey { get; private set; }

    /// <summary>
    /// Claims the hotkey system-wide. Fails harmlessly when another program already owns that
    /// combination, in which case the caller should ask the user for a different key.
    /// </summary>
    public bool TrySetHotkey(Hotkey hotkey, out string error)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        error = string.Empty;

        ReleaseHotkey();

        if (!RegisterHotKey(Handle, HotkeyId, hotkey.Modifiers, hotkey.VirtualKey))
        {
            int code = Marshal.GetLastWin32Error();
            error = code == 1409
                ? $"{hotkey.Display} is already taken by another program. Pick a different key."
                : $"Windows would not grant the hotkey {hotkey.Display} (error {code}).";
            return false;
        }

        hotkeyRegistered = true;
        CurrentHotkey = hotkey;
        return true;
    }

    public void ReleaseHotkey()
    {
        if (!hotkeyRegistered)
        {
            return;
        }

        UnregisterHotKey(Handle, HotkeyId);
        hotkeyRegistered = false;
        CurrentHotkey = null;
    }

    private void RegisterRawMouse()
    {
        RawInputDevice[] devices =
        {
            new()
            {
                UsagePage = UsagePageGeneric,
                Usage = UsageMouse,
                Flags = RidevInputSink,
                Target = Handle,
            },
        };

        RawInputAvailable = RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmInput:
                HandleRawInput(m.LParam);
                break;

            case WmHotkey:
                if ((int)m.WParam == HotkeyId)
                {
                    HotkeyPressed?.Invoke();
                }

                break;
        }

        base.WndProc(ref m);
    }

    private void HandleRawInput(IntPtr rawInputHandle)
    {
        int headerSize = Marshal.SizeOf<RawInputHeader>();
        uint size = 0;

        if (GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref size, (uint)headerSize) != 0 || size == 0)
        {
            return;
        }

        EnsureBuffer((int)size);

        uint copied = GetRawInputData(rawInputHandle, RidInput, buffer, ref size, (uint)headerSize);
        if (copied == uint.MaxValue || copied < (uint)headerSize)
        {
            return;
        }

        RawInputHeader header = Marshal.PtrToStructure<RawInputHeader>(buffer);
        if (header.Type != RimTypeMouse)
        {
            return;
        }

        RawMouse mouse = Marshal.PtrToStructure<RawMouse>(buffer + headerSize);

        // Absolute-position devices (tablets, some virtual mice) report a screen position rather
        // than a delta; counting those would be meaningless, so they are ignored.
        if ((mouse.Flags & MouseMoveAbsolute) != 0)
        {
            return;
        }

        if (mouse.LastX != 0 || mouse.LastY != 0)
        {
            MouseMoved?.Invoke(mouse.LastX, mouse.LastY);
        }
    }

    private void EnsureBuffer(int required)
    {
        if (buffer != IntPtr.Zero && bufferSize >= required)
        {
            return;
        }

        if (buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(buffer);
        }

        bufferSize = Math.Max(required, 256);
        buffer = Marshal.AllocHGlobal(bufferSize);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ReleaseHotkey();

        if (buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
            bufferSize = 0;
        }

        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    /// <summary>
    /// RAWMOUSE. The explicit padding matters: in the C struct the button union is 4-byte aligned,
    /// so it starts at offset 4, not 2.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct RawMouse
    {
        public ushort Flags;
        public ushort Padding;
        public ushort ButtonFlags;
        public ushort ButtonData;
        public uint RawButtons;
        public int LastX;
        public int LastY;
        public uint ExtraInformation;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices,
        uint deviceCount,
        uint structSize);

    [LibraryImport("user32.dll")]
    private static partial uint GetRawInputData(
        IntPtr rawInput,
        uint command,
        IntPtr data,
        ref uint dataSize,
        uint headerSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr window, int id);
}
