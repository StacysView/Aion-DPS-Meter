using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AionDPS.Ui;

/// <summary>
/// Magnetic windows over the game (Settings → Overlay, "Snap windows together", on by default): while the compact overlay or
/// a pet panel is dragged, its edges snap to the edges of the other ones and of its screen's work area (the math in
/// <see cref="WindowSnap"/>). Holding Alt lets that drag go free. The windows stay independent: pulling one away just lets
/// go, and each saves its place as before. Like SkeeveTV's OverlayDocking (1.1.0, MIT) it rewrites the rectangle Windows
/// offers in WM_MOVING, so DragMove needs no change; here everything stays in the screen pixels Windows gives (the other
/// windows by GetWindowRect, the work area by GetMonitorInfo), only the reach is scaled by the window's DPI.
/// </summary>
public static class WindowMagnet
{
    private const int WM_MOVING = 0x0216, VK_MENU = 0x12;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private static readonly List<Window> Windows = new();

    /// <summary>The settings' switch (MeterSettings.SnapWindows), set by the meter at start and on every Save.</summary>
    public static bool Enabled { get; set; } = true;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    /// <summary>A window joins the magnetic ones: it snaps while dragged, and the others snap to it while it is shown.</summary>
    public static void Attach(Window window)
    {
        if (Windows.Contains(window))
        {
            return;
        }

        Windows.Add(window);
        window.Closed += (_, _) => Windows.Remove(window);

        void Hook()
        {
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WM_MOVING && lParam != IntPtr.Zero)
                {
                    OnMoving(window, hwnd, lParam);
                }

                return IntPtr.Zero;
            });
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Hook();
        }
        else
        {
            window.SourceInitialized += (_, _) => Hook();
        }
    }

    /// <summary>The rectangle Windows is about to give the dragged window, snapped (in place, in screen pixels).</summary>
    private static void OnMoving(Window window, IntPtr hwnd, IntPtr lParam)
    {
        bool altHeld = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
        if (!WindowSnap.ShouldSnap(Enabled, altHeld))
        {
            return;
        }

        var rect = Marshal.PtrToStructure<NativeRect>(lParam);
        var moving = new SnapRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        var snapped = WindowSnap.Snap(moving, OtherRects(window), WorkArea(rect), WindowSnap.ThresholdDips * Scale(hwnd));
        if (snapped == moving)
        {
            return;
        }

        int left = (int)Math.Round(snapped.Left), top = (int)Math.Round(snapped.Top);
        rect.Right = left + (rect.Right - rect.Left);
        rect.Bottom = top + (rect.Bottom - rect.Top);
        rect.Left = left;
        rect.Top = top;
        Marshal.StructureToPtr(rect, lParam, false);
    }

    /// <summary>The other magnetic windows on show, in screen pixels.</summary>
    private static List<SnapRect> OtherRects(Window moving)
    {
        var rects = new List<SnapRect>();
        foreach (Window other in Windows)
        {
            IntPtr handle = other == moving || !other.IsVisible ? IntPtr.Zero : new WindowInteropHelper(other).Handle;
            if (handle == IntPtr.Zero || IsIconic(handle) || !GetWindowRect(handle, out NativeRect r) || r.Right <= r.Left || r.Bottom <= r.Top)
            {
                continue;
            }

            rects.Add(new SnapRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
        }

        return rects;
    }

    /// <summary>The work area (the screen without the taskbar) of the monitor the rectangle is mostly on.</summary>
    private static SnapRect? WorkArea(NativeRect rect)
    {
        IntPtr monitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        NativeRect w = info.Work;
        return new SnapRect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top);
    }

    /// <summary>Screen pixels per DIP for this window (1 at 100 %, 1.5 at 150 %...).</summary>
    private static double Scale(IntPtr hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }
}
