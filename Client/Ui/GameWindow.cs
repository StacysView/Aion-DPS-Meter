using System.Runtime.InteropServices;

namespace AionDPS.Ui;

/// <summary>Looks at the game's window and the mouse (SkeeveTV's GameWindow, MIT, cut down to what the menu watch needs): is the game in front, is the
/// mouse cursor shown, is a key or a mouse button held.</summary>
internal static class GameWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo { public int Size, Flags; public IntPtr Cursor; public Point Position; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private const int CursorShowing = 1;

    /// <summary>Whether Windows is showing a mouse cursor. The game hides it while playing (in the control mode that does) and shows it when a menu opens.
    /// A failing call counts as "not shown": the overlays then simply stay up.</summary>
    public static bool IsCursorVisible()
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        return GetCursorInfo(ref info) && (info.Flags & CursorShowing) != 0 && info.Cursor != IntPtr.Zero;
    }

    /// <summary>Whether a mouse button (left, right, middle) is held.</summary>
    public static bool MouseButtonDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0 || (GetAsyncKeyState(0x04) & 0x8000) != 0;

    /// <summary>Whether any keyboard key is held (not the lock keys, not the mouse buttons).</summary>
    public static bool AnyKeyDown()
    {
        for (int key = 1; key < 255; key++)
        {
            if (key is 0x01 or 0x02 or 0x04 or 0x05 or 0x06 or 0x14 or 0x90 or 0x91)
            {
                continue;
            }

            if ((GetAsyncKeyState(key) & 0x8000) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the window in front is the game itself (not the meter's own windows).</summary>
    public static bool ForegroundIsGame()
    {
        IntPtr hwnd = GetForegroundWindow();
        return hwnd != IntPtr.Zero && IsGame(hwnd);
    }

    private static IntPtr _known;
    private static IntPtr _notGame;
    private static long _notGameAt;

    private static bool IsGame(IntPtr hwnd)
    {
        if (hwnd == _known)
        {
            return true;
        }

        // Asked every 50 ms: a "not the game" answer is remembered for a few seconds, so the process is not looked up each time.
        long now = Environment.TickCount64;
        if (hwnd == _notGame && now - _notGameAt < 5000)
        {
            return false;
        }

        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid != (uint)Environment.ProcessId)
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                if (process.ProcessName.Equals("AION2", StringComparison.OrdinalIgnoreCase))
                {
                    _known = hwnd;
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        _notGame = hwnd;
        _notGameAt = now;
        return false;
    }
}
