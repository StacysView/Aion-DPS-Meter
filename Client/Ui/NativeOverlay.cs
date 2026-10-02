using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AionDPS.Ui;

/// <summary>
/// Win32 interop backing "Hide UI": makes the window click-through (mouse input passes to
/// whatever is behind it, e.g. the game) so the overlay doesn't steal focus/clicks, plus the
/// meter's global shortcuts (see <see cref="MeterHotkey"/>, set in Settings). The overlay one exists
/// specifically because a click-through window makes its own "restore" button unreachable by
/// definition -- without it, turning Hide UI on would be a one-way trip requiring Alt+F4 or Task
/// Manager to undo.
/// </summary>
internal sealed class NativeOverlay : IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WM_HOTKEY = 0x0312;
    private const int FirstHotkeyId = 0xA10E; // arbitrary, only needs to be unique within this process

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly IntPtr _handle;
    private readonly HashSet<MeterHotkey> _registered = new();

    /// <summary>Fired when one of the registered shortcuts is pressed, regardless of window focus.</summary>
    public event Action<MeterHotkey>? HotkeyPressed;

    /// <summary>Window must already be shown (have a native handle) before constructing this.</summary>
    public NativeOverlay(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle)
            ?? throw new InvalidOperationException("NativeOverlay requires the window to already have a native handle (construct after Show()/SourceInitialized).");
        _source.AddHook(WndProc);
    }

    /// <summary>Replaces every shortcut. Returns the ones Windows refused - a combination another
    /// program already holds.</summary>
    public IReadOnlyList<(MeterHotkey Action, Hotkey Keys)> SetHotkeys(IReadOnlyDictionary<MeterHotkey, Hotkey> hotkeys)
    {
        foreach (MeterHotkey action in _registered)
        {
            UnregisterHotKey(_handle, FirstHotkeyId + (int)action);
        }

        _registered.Clear();
        var refused = new List<(MeterHotkey, Hotkey)>();
        foreach ((MeterHotkey action, Hotkey keys) in hotkeys)
        {
            if (RegisterHotKey(_handle, FirstHotkeyId + (int)action, keys.NativeModifiers, keys.NativeKey))
            {
                _registered.Add(action);
            }
            else
            {
                refused.Add((action, keys));
            }
        }

        return refused;
    }

    public void SetClickThrough(bool enabled)
    {
        int style = GetWindowLong(_handle, GWL_EXSTYLE);
        style = enabled ? style | WS_EX_TRANSPARENT | WS_EX_LAYERED : style & ~(WS_EX_TRANSPARENT | WS_EX_LAYERED);
        SetWindowLong(_handle, GWL_EXSTYLE, style);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        int action = wParam.ToInt32() - FirstHotkeyId;
        if (msg == WM_HOTKEY && _registered.Contains((MeterHotkey)action))
        {
            HotkeyPressed?.Invoke((MeterHotkey)action);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (MeterHotkey action in _registered)
        {
            UnregisterHotKey(_handle, FirstHotkeyId + (int)action);
        }

        _source.RemoveHook(WndProc);
    }
}
