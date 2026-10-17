using System.Text.Json;
using System.Windows.Input;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>
/// The global shortcuts (Ui/Hotkey) and the panels over the game (Ui/PetPanels' OverlayPanels): the settings and map
/// shortcuts' defaults (bare F9 / F10, Stacy 2026-10-17: his skill bar holds F1 to F8), which keys may stand alone, the
/// text round trip, an older settings file, the split of the old pet panels switch and what the map shortcut does.
/// </summary>
public static class SelfCheckHotkeys
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Shortcuts and panels over the game:");
        bool ok = true;

        bool defaults = Hotkey.Defaults[MeterHotkey.OpenSettings] == new Hotkey(ModifierKeys.None, Key.F9)
            && Hotkey.Defaults[MeterHotkey.ToggleMap] == new Hotkey(ModifierKeys.None, Key.F10)
            && Hotkey.Defaults[MeterHotkey.Overlay] == new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.H)
            && Enum.GetValues<MeterHotkey>().All(a => Hotkey.Defaults.ContainsKey(a) && Hotkey.Defaults[a].IsUsable)
            && Hotkey.Defaults.Values.Distinct().Count() == Hotkey.Defaults.Count;
        Console.WriteLine($"  -> defaults: settings F9, map F10, overlay still Ctrl+Alt+H, every action has its own usable key: {defaults}");
        ok &= defaults;

        bool alone = new Hotkey(ModifierKeys.None, Key.F9).IsUsable && new Hotkey(ModifierKeys.None, Key.F1).IsUsable
            && new Hotkey(ModifierKeys.None, Key.F12).IsUsable
            && !new Hotkey(ModifierKeys.None, Key.H).IsUsable && !new Hotkey(ModifierKeys.None, Key.D5).IsUsable
            && !new Hotkey(ModifierKeys.None, Key.F13).IsUsable && !new Hotkey(ModifierKeys.None, Key.Space).IsUsable
            && new Hotkey(ModifierKeys.Control, Key.H).IsUsable && !new Hotkey(ModifierKeys.Control, Key.LeftAlt).IsUsable;
        Console.WriteLine($"  -> F1 to F12 may stand alone, a letter, a digit, F13 or Space may not: {alone}");
        ok &= alone;

        bool text = Hotkey.Defaults.Values.All(k => Hotkey.Parse(k.ToString()) == k)
            && Hotkey.Defaults[MeterHotkey.OpenSettings].ToString() == "F9" && Hotkey.Defaults[MeterHotkey.ToggleMap].ToString() == "F10"
            && Hotkey.Parse("f10") == new Hotkey(ModifierKeys.None, Key.F10)
            && Hotkey.Parse("Ctrl+F5") == new Hotkey(ModifierKeys.Control, Key.F5)
            && Hotkey.Parse("H") is null && Hotkey.Parse("7") is null && Hotkey.Parse("") is null;
        Console.WriteLine($"  -> \"F9\" / \"F10\" / \"Ctrl+F5\" read back, a bare \"H\" or \"7\" is refused: {text}");
        ok &= text;

        // A file written before the two new actions: they take their defaults, the set ones are kept; an unusable one falls back.
        var older = new MeterSettings { Hotkeys = new() { [MeterHotkey.Overlay] = "Ctrl+Shift+O", [MeterHotkey.ToggleMap] = "M" } };
        var effective = older.EffectiveHotkeys();
        bool fallback = effective[MeterHotkey.Overlay] == new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.O)
            && effective[MeterHotkey.OpenSettings] == Hotkey.Defaults[MeterHotkey.OpenSettings]
            && effective[MeterHotkey.ToggleMap] == Hotkey.Defaults[MeterHotkey.ToggleMap]
            && effective.Count == Enum.GetValues<MeterHotkey>().Length;
        var saved = new MeterSettings { Hotkeys = new() { [MeterHotkey.OpenSettings] = "F11", [MeterHotkey.ToggleMap] = "Alt+F10" } };
        var back = JsonSerializer.Deserialize<MeterSettings>(JsonSerializer.Serialize(saved))!.EffectiveHotkeys();
        bool stored = back[MeterHotkey.OpenSettings] == new Hotkey(ModifierKeys.None, Key.F11)
            && back[MeterHotkey.ToggleMap] == new Hotkey(ModifierKeys.Alt, Key.F10);
        Console.WriteLine($"  -> a file without the new actions gives F9 / F10, a bare \"M\" falls back: {fallback}; a saved F11 / Alt+F10 come back: {stored}");
        ok &= fallback && stored;

        // The old one switch: whoever had the pet panels keeps map + dots + list; whoever had none gets none.
        var wasOn = JsonSerializer.Deserialize<MeterSettings>("""{"PetPanels":true}""")!.EffectivePanels();
        var wasOff = JsonSerializer.Deserialize<MeterSettings>("""{"PetPanels":false}""")!.EffectivePanels();
        var fresh = new MeterSettings().EffectivePanels();
        bool migrated = wasOn == new OverlayPanels(true, true, true) && wasOff == new OverlayPanels(false, true, false)
            && fresh == wasOff && !wasOff.Any && wasOn.Any && wasOn.DotsOnMap;
        Console.WriteLine($"  -> old \"PetPanels\": on = map + dots + list, off (or no file) = nothing: {migrated}");
        ok &= migrated;

        var mapOnly = new MeterSettings();
        mapOnly.SetPanels(new OverlayPanels(true, false, false));
        var reread = JsonSerializer.Deserialize<MeterSettings>(JsonSerializer.Serialize(mapOnly))!;
        bool split = reread.EffectivePanels() == new OverlayPanels(true, false, false) && reread.PetPanels // an older meter still shows something
            && reread.EffectivePanels().Any && !reread.EffectivePanels().DotsOnMap
            && new OverlayPanels(false, true, true).Any && !new OverlayPanels(false, true, true).DotsOnMap
            && !new OverlayPanels(false, true, false).Any;
        Console.WriteLine($"  -> the map alone (no dots, no list) is kept and draws no dot; dots without the map draw nothing: {split}");
        ok &= split;

        // The map shortcut: shown panels hide, hidden ones come back, all off switches the map on (and shows it).
        var all = new OverlayPanels(true, true, true);
        var none = new OverlayPanels(false, false, false);
        bool toggle = OverlayPanels.Toggled(all, shownNow: true) == (all, true)
            && OverlayPanels.Toggled(all, shownNow: false) == (all, false)
            && OverlayPanels.Toggled(new OverlayPanels(false, true, true), true) == (new OverlayPanels(false, true, true), true)
            && OverlayPanels.Toggled(none, shownNow: false) == (new OverlayPanels(true, false, false), false)
            && OverlayPanels.Toggled(new OverlayPanels(false, true, false), false) == (new OverlayPanels(true, true, false), false);
        Console.WriteLine($"  -> map shortcut: hides shown panels, brings hidden ones back, all off turns the map on: {toggle}");
        ok &= toggle;

        // Checked pets, none of them on the shown map: the map says so instead of looking empty.
        var map = AionDPS.Aion2.Protocol.Aion2WorldMaps.All.FirstOrDefault();
        var dot = new TrackerDot(1, 10, 10);
        bool noChecked = map is not null
            && PetMapView.NoCheckedPetHere(new TrackerModel(map, "Z", null, [], [], Filtered: true))
            && !PetMapView.NoCheckedPetHere(new TrackerModel(map, "Z", null, [dot], [], Filtered: true))
            && !PetMapView.NoCheckedPetHere(new TrackerModel(map, "Z", null, [], [], Filtered: false))
            && !PetMapView.NoCheckedPetHere(new TrackerModel(null, "", null, [], [], Filtered: true))
            && !PetMapView.NoCheckedPetHere(null);
        Console.WriteLine($"  -> checked pets with no dot on the shown map give the \"no checked pet on this map\" line (only then): {noChecked}");
        ok &= noChecked;

        return ok;
    }
}
