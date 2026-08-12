using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using TestGiveMeSpace.Core;

namespace TestGiveMeSpace.Server;

internal static class NativeMethods
{
    private const string GuardSoundFileName = "guard.wav";
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint MbIconAsterisk = 0x00000040;
    private const uint SndAsync = 0x0001;
    private const uint SndFilename = 0x00020000;
    private const uint SndNodefault = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int PlaqueMargin = 16;
    private const uint MonitorDefaultToNearest = 2;

    public static void ApplyNoActivateStyle(IntPtr handle)
    {
        nint style = GetWindowLongPtr(handle, GwlExStyle);
        SetWindowLongPtr(handle, GwlExStyle, style | (nint)(WsExNoActivate | WsExToolWindow));
    }

    public static void PlayGuardSound()
    {
        string soundPath = Path.Combine(AppContext.BaseDirectory, GuardSoundFileName);
        if (!PlaySound(soundPath, IntPtr.Zero, SndFilename | SndAsync | SndNodefault))
        {
            MessageBeep(MbIconAsterisk);
        }
    }

    public static Point GetCursorPosition()
        => GetCursorPos(out NativePoint point)
            ? new Point(point.X, point.Y)
            : new Point(double.NaN, double.NaN);

    public static IReadOnlyList<MonitorWorkingArea> GetMonitorWorkingAreas()
    {
        List<MonitorWorkingArea> result = [];
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                result.Add(new MonitorWorkingArea(
                    monitor,
                    info.WorkArea.Left,
                    info.WorkArea.Top,
                    info.WorkArea.Right,
                    info.WorkArea.Bottom,
                    info.DeviceName));
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static IntPtr GetWindowMonitor(IntPtr handle)
        => MonitorFromWindow(handle, MonitorDefaultToNearest);

    public static void PositionAtTopRight(
        IntPtr handle,
        MonitorWorkingArea workingArea,
        GuardPosition? savedPosition)
    {
        if (!GetWindowRect(handle, out NativeRect windowRect))
        {
            return;
        }

        int width = windowRect.Right - windowRect.Left;
        int height = windowRect.Bottom - windowRect.Top;
        int defaultLeft = workingArea.Right - width - PlaqueMargin;
        int defaultTop = workingArea.Top + PlaqueMargin;
        int left = (int)Math.Round(savedPosition?.Left ?? defaultLeft);
        int top = (int)Math.Round(savedPosition?.Top ?? defaultTop);
        left = Math.Clamp(left, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - width));
        top = Math.Clamp(top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - height));
        SetWindowPos(
            handle,
            IntPtr.Zero,
            left,
            top,
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    public static bool TryGetWindowPosition(IntPtr handle, out int left, out int top)
    {
        if (GetWindowRect(handle, out NativeRect windowRect))
        {
            left = windowRect.Left;
            top = windowRect.Top;
            return true;
        }

        left = 0;
        top = 0;
        return false;
    }

    public static bool TryGetWindowBounds(IntPtr handle, out int left, out int top, out int width, out int height)
    {
        if (GetWindowRect(handle, out NativeRect windowRect))
        {
            left = windowRect.Left;
            top = windowRect.Top;
            width = windowRect.Right - windowRect.Left;
            height = windowRect.Bottom - windowRect.Top;
            return true;
        }

        left = 0;
        top = 0;
        width = 0;
        height = 0;
        return false;
    }

    public static bool TryMoveWindow(IntPtr handle, int left, int top)
        => SetWindowPos(
            handle,
            IntPtr.Zero,
            left,
            top,
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);

    private static nint GetWindowLongPtr(IntPtr handle, int index)
        => IntPtr.Size == 8
            ? GetWindowLongPtr64(handle, index)
            : GetWindowLong32(handle, index);

    private static nint SetWindowLongPtr(IntPtr handle, int index, nint newLong)
        => IntPtr.Size == 8
            ? SetWindowLongPtr64(handle, index, newLong)
            : SetWindowLong32(handle, index, newLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr64(IntPtr handle, int index, nint newLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern nint GetWindowLong32(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern nint SetWindowLong32(IntPtr handle, int index, nint newLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint type);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;

    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private delegate bool MonitorEnumProc(
        IntPtr monitor,
        IntPtr deviceContext,
        IntPtr monitorRect,
        IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRect,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}

internal readonly record struct MonitorWorkingArea(
    IntPtr MonitorHandle,
    int Left,
    int Top,
    int Right,
    int Bottom,
    string DeviceName);
