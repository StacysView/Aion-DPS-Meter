using System.IO;
using System.Text.Json;

namespace AionDPS.Ui;

/// <summary>
/// Settings for the meter UI. Scoped deliberately to what the tool actually has a data source for.
/// </summary>
public sealed class MeterSettings
{

    // User interface
    public string Theme { get; set; } = "Dark";
    public string FontSize { get; set; } = "Medium";

    /// <summary>Share-of-group bar under each row's Damage/DPS line (see PlayerRow.SharePercent).</summary>
    public bool ShowShareBars { get; set; } = true;

    /// <summary>Hide UI shows one compact panel - the fought target with its hit points, then a
    /// line per player with DPS, share and total - instead of a chip per player, follows the newest
    /// boss fight by itself, and opens a player's skill breakdown on a click. On by default; off
    /// brings back the click-through chips.</summary>
    public bool CompactOverlay { get; set; } = true;

    /// <summary>Start from zero when a fight begins after <see cref="AutoResetSeconds"/>
    /// without any damage: the meter shows the current fight, the previous one stays readable
    /// until then and is kept in the fight history.</summary>
    public bool AutoReset { get; set; } = true;

    /// <summary>Seconds without damage after which the next fight starts from zero (1 to 600).</summary>
    public int AutoResetSeconds { get; set; } = 10;

    /// <summary>Whose rows the meter shows: "All" (everybody around), "Group" (you and your party) or
    /// "Raid" (your party and the one it is joined with). Switched in the overlay.</summary>
    public string ViewScope { get; set; } = "Group";

    /// <summary>The boss's health bar in the compact overlay (current / full, %, HP check mark).</summary>
    public bool ShowBossHp { get; set; }

    /// <summary>The Rift's countdown in the overlay's footer (Schedule/GameEventTimers).</summary>
    public bool ShowRiftTimer { get; set; } = true;

    /// <summary>The Shugo Festival's countdown in the overlay's footer.</summary>
    public bool ShowShugoTimer { get; set; } = true;

    /// <summary>Reminders in the middle of the screen 5 minutes before the Shugo Festival opens and on
    /// the hour (Schedule/GameEventTimers.ShugoReminder) - off unless asked for (Stacy, 2026-10-13).</summary>
    public bool ShugoReminder { get; set; }

    /// <summary>A beep like a parking sensor while the game announces a Hidden Cube nearby (Ui/CubeSound) - on by default
    /// (Stacy, 2026-10-17: "très important").</summary>
    public bool CubeSound { get; set; } = true;

    /// <summary>The cube beep: one of <c>CubeSound.Kinds</c>.</summary>
    public string CubeSoundKind { get; set; } = "ping";

    public double CubeSoundVolume { get; set; } = 0.7;

    /// <summary>"Cube caché à 23 m" in the overlay's footer while one is near.</summary>
    public bool CubeDistance { get; set; } = true;

    /// <summary>The watched field bosses' countdowns in the overlay's footer (Schedule/FieldBossTimers).</summary>
    public bool ShowFieldBossTimer { get; set; } = true;

    /// <summary>A reminder in the middle of the screen 5 minutes before a watched field boss comes back, and when it does.</summary>
    public bool FieldBossReminder { get; set; }

    private List<int> _watchedFieldBosses = [2101074];

    /// <summary>The NPC ids of the field bosses followed in the footer (default: Eternal Gartua). A saved list
    /// replaces the default as a whole (the setter assigns, the deserializer never appends to a pre-filled list).</summary>
    public List<int> WatchedFieldBosses
    {
        get => _watchedFieldBosses;
        set => _watchedFieldBosses = value ?? [];
    }

    /// <summary>The banner at a boss's death - MVP, deaths, low DPS (Combat/LowDpsTease, Ui/TeaseBanner).
    /// Its rules are fixed (Stacy, 2026-10-11): just this switch and <see cref="TeaseSound"/>.</summary>
    public bool TeaseLowDps { get; set; } = true;

    /// <summary>The banner's sad trombone (Ui/TeaseSound).</summary>
    public bool TeaseSound { get; set; } = true;

    /// <summary>A pet's line in the pet list panel and its card in the Pets window light up in purple for a moment when
    /// it gains souls or a level (Aion2PetCollection.Changed). Replaced the banner and fanfare of 0.9.7x (Stacy,
    /// 2026-10-17): nothing is announced or played for pets any more.</summary>
    public bool PetFlash { get; set; } = true;

    /// <summary>Pet ids pinned in the Pets window (what the player is hunting right now).</summary>
    public List<int> PetPins { get; set; } = new();

    /// <summary>Key of the farm zone chosen in the Pets window (Aion2PetZones); null = none.</summary>
    public string? PetFarmZone { get; set; }

    /// <summary>The pet map and pet list panels over the game (Ui/PetPanels). Off unless asked for.</summary>
    public bool PetPanels { get; set; }

    /// <summary>Locked pet panels let every click through to the game; unlocked they can be dragged, the map zoomed with
    /// the wheel and its dots hovered for the pet's name.</summary>
    public bool PetPanelsLocked { get; set; }

    /// <summary>How opaque the pet panels' dark background is, 0.2 to 1.</summary>
    public double PetPanelsOpacity { get; set; } = 0.85;

    /// <summary>Where the pet panels were dragged to (screen DIPs); null = the default place, right of the screen.</summary>
    public double? PetMapLeft { get; set; }
    public double? PetMapTop { get; set; }
    public double? PetListLeft { get; set; }
    public double? PetListTop { get; set; }

    /// <summary>The pet list's size as dragged by its corner grip (DIPs of its content): width 200 to 700; height null =
    /// as tall as its rows (10 at most), else 100 to 1200 with a scroll for the rest.</summary>
    public double PetListWidth { get; set; } = 250;
    public double? PetListHeight { get; set; }

    /// <summary>Side of the square pet map in px (1 px = 1 m at 100 % zoom), 160 to 600: the Settings slider, or the
    /// map panel's corner grip.</summary>
    public double PetMapSize { get; set; } = 260;

    /// <summary>The pet map's zoom, 0.5 to 2 (the wheel on the unlocked map); 1 = 1 px per metre.</summary>
    public double PetMapZoom { get; set; } = 1.0;

    /// <summary>The volume of every sound of the banner, 0 to 1 (a slider, Stacy, 2026-10-15). Half by
    /// default: what the sound files always played at.</summary>
    public double SoundVolume { get; set; } = 0.5;

    /// <summary>Size of the compact overlay, as a factor of its 320 px design (its corner grip).</summary>
    public double OverlayScale { get; set; } = 1.0;

    /// <summary>How opaque the overlay's dark background is, 0.2 (see-through) to 1 (solid).</summary>
    public double OverlayOpacity { get; set; } = 0.6;

    /// <summary>The global shortcuts, as "Ctrl+Alt+H" (see Hotkey.Parse); missing or unusable
    /// entries fall back to the defaults.</summary>
    public Dictionary<MeterHotkey, string> Hotkeys { get; set; } = new();

    public IReadOnlyDictionary<MeterHotkey, Hotkey> EffectiveHotkeys() =>
        Enum.GetValues<MeterHotkey>().ToDictionary(
            action => action,
            action => Hotkey.Parse(Hotkeys.GetValueOrDefault(action)) ?? Hotkey.Defaults[action]);

    /// <summary>"↓ N" damage-received figure on each row's second line (see PlayerRow.DamageTaken).
    /// Off by default, per the user: most players never want this second line at all, so the row
    /// stays at its narrower single-line height until someone opts in.</summary>
    public bool ShowDamageTaken { get; set; }



    /// <summary>Whether finished fights are filed into the local history (History/FightRecorder,
    /// %AppData%\Aion DPS Meter\fights.db). Local only - nothing about it is ever uploaded.</summary>
    public bool RecordFightHistory { get; set; } = true;

    /// <summary>History retention: fights older than this, or beyond the newest
    /// <see cref="HistoryMaxFights"/>, are pruned at startup.</summary>
    public int HistoryRetentionDays { get; set; } = 90;

    public int HistoryMaxFights { get; set; } = 2000;

    /// <summary>GUI display language, an ISO 639-1 code from LocalizationManager.SupportedLanguages
    /// (e.g. "de"), or "" on a fresh install to mean "use whatever LocalizationManager already
    /// auto-detected from the OS at startup, and don't overwrite it here." Independent of Chat.log's
    /// own language (see ChatLogParser's multi-language remarks and Localization.cs) -- this is
    /// purely which language the meter's OWN menus/buttons/labels render in.</summary>
    public string Language { get; set; } = "";

    /// <summary>Whether the meter window starts pinned above every other window, including the game
    /// itself. Defaults to off -- per the user, existing behaviour (an ordinary window, until turned
    /// on from the View menu's "Always on top" toggle) should not change for anyone who never asked
    /// for this. That toggle and this setting are the same value now, not two independent switches:
    /// checking one updates the other and saves immediately, the same save-on-change treatment
    /// CheckForUpdates already gets, so "how I last left it" is what a fresh launch restores.</summary>
    public bool AlwaysOnTopOnStartup { get; set; }

    /// <summary>MainWindow's size/position, saved on close and restored on next launch -- found
    /// necessary by the user, who resized the window and had it reset every restart. All four
    /// null (fresh install / older settings file) means "use the XAML default", not "0x0 at the
    /// origin".</summary>
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    /// <summary>Settings window's own size, saved on close - per the user, who resized it (the
    /// Characters tab needs real room) and had it reset every time. Position isn't remembered on
    /// purpose: it always opens CenterOwner'd on MainWindow instead, which stays correct
    /// regardless of where MainWindow itself currently is; only WindowWidth/Height above (the
    /// MAIN window) also remember position, since that one has nothing to center against.</summary>
    public double? SettingsWindowWidth { get; set; }
    public double? SettingsWindowHeight { get; set; }


    /// <summary>Whether the meter asks GitHub for a newer release -- at startup and every five
    /// minutes while it runs (see MainWindow's update timer). Default on, but a real switch and
    /// not a decorative one: this is the program's only outbound network call, and the README
    /// promises that nothing leaves the machine, so anyone who wants that promise kept literally
    /// can turn it off. The "Check for updates" menu item still works when it is off -- that one
    /// is the user asking, not the program deciding.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Whether the own character profile is uploaded by itself a few seconds after a login
    /// (see MainWindow.ScheduleOwnProfileUpload), so the player can be found on the website. On by
    /// default; the manual upload button works either way.</summary>
    public bool AutoUploadProfile { get; set; } = true;


    /// <summary>Network adapter the Aion 2 packet capture listens on (Aion2/Capture/CaptureAdapters):
    /// null/empty = automatic (the adapter Windows routes internet traffic through), "all" = every
    /// adapter, else a NetworkInterface.Id. Matters when a gaming VPN carries the game's traffic.</summary>
    public string? CaptureAdapterId { get; set; }

    /// <summary>The user's own Aion 2 character name. Aion 2 frames name every player but give no
    /// hint which one is you, so the meter matches this name; it fills it in by itself as soon as
    /// the stream reveals it (a party roster), and Settings lets it be typed for solo play.</summary>
    public string? Aion2CharacterName { get; set; }

    /// <summary>What the packet capture is started with (MainWindow.StartCapture): Save restarts the
    /// capture only when this changed. A restart for nothing ended the running recording (a 5v5 arena
    /// capture of half a second, 2026-10-13) and forgot the group and the names. Empty = none.</summary>
    public (string? Adapter, string? Name) CaptureKey() =>
        (string.IsNullOrWhiteSpace(CaptureAdapterId) ? null : CaptureAdapterId,
         string.IsNullOrWhiteSpace(Aion2CharacterName) ? null : Aion2CharacterName.Trim());








    /// <summary>
    /// Per-user settings location, NOT next to the exe. Beside the exe is where a self-updating
    /// install is least safe to keep anything: Velopack swaps the whole application directory when
    /// an update applies, so settings written there would be replaced along with it. (The same
    /// path was already wrong for the older per-machine MSI, which put the exe under Program Files
    /// where an unprivileged process cannot write at all.)
    /// </summary>
    private static string SettingsPath => Path.Combine(AppDataFolder.Path, "meter-settings.json");

    /// <summary>Where older, elevated builds kept the file. Read once, on first launch after the
    /// upgrade, so an existing Aion folder and character list survive the move instead of the
    /// user finding an empty settings dialog.</summary>
    private static string LegacySettingsPath => Path.Combine(AppContext.BaseDirectory, "meter-settings.json");

    public static MeterSettings Load()
    {
        try
        {
            string path = File.Exists(SettingsPath) ? SettingsPath
                : File.Exists(LegacySettingsPath) ? LegacySettingsPath
                : SettingsPath;

            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<MeterSettings>(File.ReadAllText(path));
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to defaults -- a corrupt settings file should not block the app from starting.
        }

        return new MeterSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
