using System.Windows.Input;

namespace AionDPS.Ui;

/// <summary>The meter's global shortcuts - each one works from inside the game.</summary>
public enum MeterHotkey
{
    /// <summary>Overlay on/off (the full window back).</summary>
    Overlay,
    /// <summary>Start the meter from zero.</summary>
    Reset,
    /// <summary>Damage / healing.</summary>
    Mode,
    /// <summary>Pause / resume recording.</summary>
    Pause,
    /// <summary>Opens the Settings window (or brings it to the front).</summary>
    OpenSettings,
    /// <summary>Shows / hides the panels over the game (the map, and the pet list when it is on).</summary>
    ToggleMap,
}

/// <summary>
/// One key combination, as Settings stores it ("Ctrl+Alt+H", "F9"). A global shortcut needs at least
/// one modifier, except a function key F1 to F12 alone (Stacy, 2026-10-17): Windows takes the key away
/// from the game while it is registered, so a bare letter or digit would break typing in the chat.
/// </summary>
public sealed record Hotkey(ModifierKeys Modifiers, Key Key)
{
    public static readonly IReadOnlyDictionary<MeterHotkey, Hotkey> Defaults = new Dictionary<MeterHotkey, Hotkey>
    {
        [MeterHotkey.Overlay] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.H),
        [MeterHotkey.Reset] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.R),
        [MeterHotkey.Mode] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.M),
        [MeterHotkey.Pause] = new(ModifierKeys.Control | ModifierKeys.Alt, Key.P),
        // Bare: Stacy's skill bar holds F1 to F8 (a registered key no longer reaches the game).
        [MeterHotkey.OpenSettings] = new(ModifierKeys.None, Key.F9),
        [MeterHotkey.ToggleMap] = new(ModifierKeys.None, Key.F10),
    };

    public bool IsUsable => (Modifiers != ModifierKeys.None || Key is >= Key.F1 and <= Key.F12) && Key is not (Key.None or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System);

    /// <summary>Win32 RegisterHotKey modifier flags.</summary>
    public uint NativeModifiers =>
        (Modifiers.HasFlag(ModifierKeys.Alt) ? 0x1u : 0) | (Modifiers.HasFlag(ModifierKeys.Control) ? 0x2u : 0)
        | (Modifiers.HasFlag(ModifierKeys.Shift) ? 0x4u : 0) | (Modifiers.HasFlag(ModifierKeys.Windows) ? 0x8u : 0)
        | 0x4000u; // MOD_NOREPEAT: holding the keys fires once

    public uint NativeKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
        _ => key.ToString(),
    };

    /// <summary>"Ctrl+Alt+H" back into a combination; null for anything unusable.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var modifiers = ModifierKeys.None;
        Key key = Key.None;
        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control" or "strg":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift" or "maj":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (raw.Length == 1 && char.IsDigit(raw[0]))
                    {
                        key = Key.D0 + (raw[0] - '0');
                    }
                    else if (raw.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && raw.Length == 4 && char.IsDigit(raw[3]))
                    {
                        key = Key.NumPad0 + (raw[3] - '0');
                    }
                    else if (!Enum.TryParse(raw, ignoreCase: true, out key))
                    {
                        return null;
                    }

                    break;
            }
        }

        var hotkey = new Hotkey(modifiers, key);
        return hotkey.IsUsable ? hotkey : null;
    }
}
