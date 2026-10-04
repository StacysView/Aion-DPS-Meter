# Recent fights history and players' hits in Taken mode - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the last 10 overlay fights (opened from an overlay button, in a window beside the live overlay) and count outside players' hits in Taken mode.

**Architecture:** Two pure helpers carry the rules and are covered by the self-test: `Combat/TakenHits.cs` (which hit is damage taken) and `History/RecentFights.cs` (one overlay fight as a `FightDetail`, store pruned to 10). `MainWindow` saves the fight on every reset/close into `recent-fights.db` through the existing `FightStore`, and the existing `FightHistoryWindow` is slimmed down to list them and open a player's skills.

**Tech Stack:** C# / .NET 10 WPF, Microsoft.Data.Sqlite (existing `FightStore`), self-test harness (`dotnet "Aion DPS.dll" selftest`).

## Global Constraints

- Spec: `docs/superpowers/specs/2026-10-04-recent-fights-and-pvp-taken-design.md`.
- Keep the newest **10** fights; a fight counts if it lasted **at least 10 s** and holds damage.
- Database file: `recent-fights.db` in `AppDataFolder.Path`; the old `fights.db` is never written or deleted.
- Taken counts monsters' hits and hits from players outside the local player's group; never the target's own hits, never a teammate's, never heals.
- All new UI strings in the 8 languages of `Client/assets/i18n/ui_strings.json` (en, de, fr, es, ru, pl, tr, zh).
- No injection, no memory reading, no packet sent; decoding is not touched by this plan.
- Build: `dotnet build Client -c Release -v q -nologo` from `C:\dev\Aion-DPS-Meter`. Self-test: `dotnet "Aion DPS.dll" selftest` from `Client\bin\Release\net10.0-windows` (ends with `ALL CHECKS PASSED`). Never launch the app without arguments (UAC prompt).
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Taken counts outside players' hits

**Files:**
- Create: `Client/Combat/TakenHits.cs`
- Modify: `Client/Ui/MainWindow.xaml.cs` (`HostileHitsTaken`, around line 1151)
- Test: `Client/Combat/SelfCheckHistory.cs` (new scenario `RunTakenHitsScenario`)

**Interfaces:**
- Produces: `public static bool TakenHits.IsHostile(DamageEvent ev, Func<int, bool> isPlayer, Func<int, bool> isTeammate)`; `MainWindow.IsTeammate(int id)` (private).

- [ ] **Step 1: Write the failing test** - in `SelfCheckHistory.Run()` add `ok &= RunTakenHitsScenario();` after `RunRecorderAndStoreScenario();`, and add the method:

```csharp
    /// <summary>Taken mode's rule on synthetic hits: a monster's and an outside player's hits count,
    /// one's own, a teammate's, a heal and a hit on a monster do not.</summary>
    private static bool RunTakenHitsScenario()
    {
        Console.WriteLine("[selftest] Taken: monsters' and outside players' hits, never one's own or a teammate's:");
        const int Me = 1, Mate = 2, Foe = 3, Monster = 100;
        var players = new HashSet<int> { Me, Mate, Foe };
        var mates = new HashSet<int> { Me, Mate };
        DateTime t = new(2026, 10, 4, 20, 45, 0);
        bool Counts(int source, int target, bool heal = false) =>
            TakenHits.IsHostile(new DamageEvent(t, source, target, 100, heal), players.Contains, mates.Contains);
        bool ok = Counts(Monster, Me) && Counts(Foe, Me) && !Counts(Me, Me) && !Counts(Mate, Me)
            && !Counts(Foe, Me, heal: true) && !Counts(Me, Monster);
        Console.WriteLine($"  -> monster yes, outside player yes, self no, teammate no, heal no, on a monster no: {ok}");
        return ok;
    }
```

- [ ] **Step 2: Build to verify it fails** - `dotnet build Client -c Release -v q -nologo`. Expected: error CS0103 `TakenHits` does not exist.

- [ ] **Step 3: Write the helper** - `Client/Combat/TakenHits.cs`:

```csharp
namespace AionDPS.Combat;

/// <summary>
/// Which hits are damage a player took (Taken mode, the history's taken column): a hostile hit on a
/// player - from a monster, or from a player outside their group (the opponent of a 1v1 arena,
/// 2026-10-04 20:45). Never their own hit (a skill's hit-point cost, the arena's round reset
/// frames) and never a teammate's (a monster's "Attack" credited to a party member on Thamon,
/// 2026-10-04 00:31; a Cleric's heal read as damage).
/// </summary>
public static class TakenHits
{
    public static bool IsHostile(DamageEvent ev, Func<int, bool> isPlayer, Func<int, bool> isTeammate) =>
        !ev.IsHeal && isPlayer(ev.TargetObjectId) && ev.SourceObjectId != ev.TargetObjectId
        && (!isPlayer(ev.SourceObjectId) || !isTeammate(ev.SourceObjectId));
}
```

- [ ] **Step 4: Use it in Taken mode** - in `MainWindow.xaml.cs` replace `HostileHitsTaken` and its doc comment with:

```csharp
    /// <summary>The hostile hits a player took inside a span: a monster's, or an outside player's
    /// (see <see cref="TakenHits"/>).</summary>
    private List<DamageEvent> HostileHitsTaken((DateTime Start, DateTime End)? span, int? playerId = null) =>
        _aggregator.Events
            .Where(ev => (playerId is not int id || ev.TargetObjectId == id)
                && TakenHits.IsHostile(ev, IsPlayerName, IsTeammate)
                && (span is not (DateTime from, DateTime to) || (ev.Timestamp >= from && ev.Timestamp <= to)))
            .ToList();

    /// <summary>A member of the local player's group - or, until the roster is read, a player
    /// fighting the same monsters (the Group scope's own fallback, see <see cref="IsInScope"/>).</summary>
    private bool IsTeammate(int id) =>
        _source?.Entities is Aion2.Aion2EntityDirectory directory
        && (IsGroupMember(id, directory) || (!RosterKnown(directory) && _alongsideIds.Contains(id)));
```

- [ ] **Step 5: Build and run the self-test** - build, then from `Client\bin\Release\net10.0-windows`: `dotnet "Aion DPS.dll" selftest`. Expected: the new line ends with `: True`, last line `ALL CHECKS PASSED`.

- [ ] **Step 6: Commit**

```bash
git add Client/Combat/TakenHits.cs Client/Combat/SelfCheckHistory.cs Client/Ui/MainWindow.xaml.cs
git commit -m "Taken mode counts outside players' hits (PvP), never one's own or a teammate's"
```

### Task 2: One overlay fight as a history entry

**Files:**
- Create: `Client/History/RecentFights.cs`
- Test: `Client/Combat/SelfCheckHistory.cs` (new scenario `RunRecentFightsScenario`)

**Interfaces:**
- Consumes: `TakenHits.IsHostile` (Task 1); existing `FightContext`, `FightDetail`, `FightSummary`, `FightParticipant`, `FightStore.Insert/Prune/Count`.
- Produces: `RecentFights.Keep` (10), `RecentFights.MinDuration` (10 s), `RecentFights.DefaultPath`, `RecentFights.Describe(IReadOnlyList<DamageEvent> events, string title, FightContext context, Func<int, bool> isShown, Func<int, bool> isTeammate, Func<int, bool> isSummon) : FightDetail?`, `RecentFights.Save(FightStore store, FightDetail detail)`.

- [ ] **Step 1: Write the failing test** - in `SelfCheckHistory.Run()` add `ok &= RunRecentFightsScenario();`, and add:

```csharp
    /// <summary>An overlay fight as a history entry (synthetic): the shown players only, damage, DPS
    /// over the span, healing, hits taken from a monster and an outside player; a 5 s poke is no
    /// fight; the store keeps the newest ten.</summary>
    private static bool RunRecentFightsScenario()
    {
        Console.WriteLine("[selftest] Recent fights: one overlay fight as a history entry, the newest ten kept:");
        const int Me = 1, Mate = 2, Stranger = 3, Foe = 4, Boss = 100;
        var players = new HashSet<int> { Me, Mate, Stranger, Foe };
        var shown = new HashSet<int> { Me, Mate };
        DateTime t = new(2026, 10, 4, 21, 0, 0);
        var events = new List<DamageEvent>();
        for (int i = 0; i <= 20; i++)
        {
            events.Add(new DamageEvent(t.AddSeconds(i), Me, Boss, 1000, IsHeal: false, Skill: "Combustion"));
        }

        events.Add(new DamageEvent(t.AddSeconds(5), Stranger, Boss, 5000, IsHeal: false));
        events.Add(new DamageEvent(t.AddSeconds(6), Mate, Me, 700, IsHeal: true, Skill: "Healing Light"));
        events.Add(new DamageEvent(t.AddSeconds(7), Boss, Me, 300, IsHeal: false, Skill: "Attack"));
        events.Add(new DamageEvent(t.AddSeconds(8), Foe, Me, 200, IsHeal: false, Skill: "Tempest Shot"));
        var context = new FightContext(id => id switch { Me => "Me", Mate => "Mate", Stranger => "Stranger", Foe => "Foe", _ => "Boss" },
            _ => "", _ => "", players.Contains, id => id == Me, _ => false, _ => false, "aion2", null);
        FightDetail? fight = RecentFights.Describe(events, "Boss", context, shown.Contains, shown.Contains, _ => false);
        FightParticipant? me = fight?.Participants.FirstOrDefault(p => p.Name == "Me");
        FightParticipant? mate = fight?.Participants.FirstOrDefault(p => p.Name == "Mate");
        bool described = fight is not null && fight.Participants.Count == 2 && me is not null && mate is not null
            && me.Damage == 21_000 && Math.Abs((me.Dps ?? 0) - 1050) < 0.5 && me.DamageTaken == 500
            && mate.Healing == 700 && fight.Summary.TargetName == "Boss" && fight.Summary.Duration == TimeSpan.FromSeconds(20);
        var poke = events.Where(ev => ev.Timestamp <= t.AddSeconds(5)).ToList();
        bool tooShort = RecentFights.Describe(poke, "Boss", context, shown.Contains, shown.Contains, _ => false) is null;

        string path = Path.Combine(Path.GetTempPath(), $"aiondps-recent-{Guid.NewGuid():N}.db");
        bool keptTen;
        using (var store = new FightStore(path))
        {
            for (int i = 0; i < 12; i++)
            {
                var shifted = events.Select(ev => ev with { Timestamp = ev.Timestamp.AddMinutes(i) }).ToList();
                RecentFights.Save(store, RecentFights.Describe(shifted, $"Fight {i}", context, shown.Contains, shown.Contains, _ => false)!);
            }

            var left = store.Query(null, 50);
            keptTen = left.Count == 10 && left[0].TargetName == "Fight 11" && left.All(f => f.TargetName != "Fight 0");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TryDelete(path);
        Console.WriteLine($"  -> shown players with damage/DPS/healing/taken: {described}; a 5 s poke is no fight: {tooShort}; newest ten kept: {keptTen}");
        return described && tooShort && keptTen;
    }
```

(`TryDelete` already exists in `SelfCheckHistory.cs` if the recorder scenario deletes its temp file; if it does not, add it:)

```csharp
    private static void TryDelete(string path)
    {
        foreach (string file in new[] { path, path + "-wal", path + "-shm" })
        {
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
```

- [ ] **Step 2: Build to verify it fails** - expected: error CS0103 `RecentFights` does not exist.

- [ ] **Step 3: Write `Client/History/RecentFights.cs`**

```csharp
using System.IO;
using AionDPS.Combat;

namespace AionDPS.History;

/// <summary>
/// The last few overlay fights - everything between two resets of the meter, as the overlay showed
/// it - kept in recent-fights.db. FightRecorder filed one entry per monster instead: a 10:37
/// capture (2026-10-04) gave 16 pieces of trash and two bosses split in parts.
/// </summary>
public static class RecentFights
{
    public const int Keep = 10;

    public static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(10);

    public static string DefaultPath => Path.Combine(AppDataFolder.Path, "recent-fights.db");

    /// <summary>
    /// The fight as a history entry, or null when its damage spans less than <see cref="MinDuration"/>.
    /// Damage is the players' hits on anything but themselves and their own side; DPS runs over the
    /// span from the first to the last of them. <paramref name="isShown"/> is the overlay's scope
    /// (only the players it listed), <paramref name="isTeammate"/> the local player's group (see
    /// <see cref="TakenHits"/>), <paramref name="isSummon"/> leaves heals on summons out, as Heal
    /// mode does.
    /// </summary>
    public static FightDetail? Describe(IReadOnlyList<DamageEvent> events, string title, FightContext context,
        Func<int, bool> isShown, Func<int, bool> isTeammate, Func<int, bool> isSummon)
    {
        var damage = events.Where(ev => !ev.IsHeal && context.IsPlayer(ev.SourceObjectId) && ev.TargetObjectId != ev.SourceObjectId
                && !(context.IsPlayer(ev.TargetObjectId) && isTeammate(ev.TargetObjectId) && isTeammate(ev.SourceObjectId)))
            .ToList();
        if (damage.Count == 0)
        {
            return null;
        }

        DateTime start = damage.Min(ev => ev.Timestamp);
        DateTime end = damage.Max(ev => ev.Timestamp);
        if (end - start < MinDuration)
        {
            return null;
        }

        double seconds = (end - start).TotalSeconds;
        var taken = events.Where(ev => TakenHits.IsHostile(ev, context.IsPlayer, isTeammate)).ToList();
        var heals = events.Where(ev => ev.IsHeal && context.IsPlayer(ev.SourceObjectId) && !isSummon(ev.TargetObjectId)).ToList();
        var participants = damage.Select(ev => ev.SourceObjectId)
            .Concat(heals.Select(ev => ev.SourceObjectId))
            .Concat(taken.Select(ev => ev.TargetObjectId))
            .Distinct()
            .Where(isShown)
            .Select(id =>
            {
                long dealt = damage.Where(ev => ev.SourceObjectId == id).Sum(ev => ev.Amount);
                return new FightParticipant(context.NameOf(id), context.ClassOf(id), context.FactionOf(id), context.IsSelf(id),
                    context.IsEnemy(id), dealt, dealt / seconds,
                    heals.Where(ev => ev.SourceObjectId == id).Sum(ev => ev.Amount),
                    taken.Where(ev => ev.TargetObjectId == id).Sum(ev => ev.Amount));
            })
            .OrderByDescending(p => p.Damage)
            .ToList();

        var names = events.SelectMany(ev => new[] { ev.SourceObjectId, ev.TargetObjectId })
            .Distinct()
            .ToDictionary(id => id, context.NameOf);
        var summary = new FightSummary(0, context.Game, context.ServerName, start, end, title, "overlay",
            participants.Sum(p => p.Damage), participants.Count, participants.FirstOrDefault(p => p.IsSelf)?.Name);
        return new FightDetail(summary, participants, events.OrderBy(ev => ev.Timestamp).ToList(), names);
    }

    /// <summary>Files the fight and keeps the newest <see cref="Keep"/>.</summary>
    public static void Save(FightStore store, FightDetail detail)
    {
        store.Insert(detail);
        store.Prune(retentionDays: 36_500, maxFights: Keep);
    }
}
```

- [ ] **Step 4: Build and run the self-test** - expected: `-> shown players with damage/DPS/healing/taken: True; a 5 s poke is no fight: True; newest ten kept: True`, then `ALL CHECKS PASSED`.

- [ ] **Step 5: Commit**

```bash
git add Client/History/RecentFights.cs Client/Combat/SelfCheckHistory.cs
git commit -m "History: one overlay fight as an entry, the newest ten kept (RecentFights)"
```

### Task 3: The meter files every overlay fight

**Files:**
- Modify: `Client/Ui/MainWindow.xaml.cs` (`InitializeFightHistory` ~605, `RecordFinishedFights` ~628, `OnFightHistoryClicked` ~685, `EnterHistoryMode` ~712 removed)

**Interfaces:**
- Consumes: `RecentFights.Describe/Save/DefaultPath/Keep` (Task 2), `IsTeammate` (Task 1), existing `IsInScope`, `BuildFightContext`, `OverlayTargetText`.
- Produces: `_fightStore` now holds `recent-fights.db`; `_fightRecorder` is never created.

- [ ] **Step 1: Open the recent store instead of the per-monster one** - in `InitializeFightHistory` replace the three lines in the `try` with:

```csharp
            _fightStore = new FightStore(RecentFights.DefaultPath);
            _fightStore.Prune(retentionDays: 36_500, maxFights: RecentFights.Keep);
```

- [ ] **Step 2: Save the overlay fight on reset and close** - replace the body of `RecordFinishedFights` (keep its signature; callers pass `flushAll: true` on Clear, auto-reset and exit, `false` from the 5 s timer) and its doc comment with:

```csharp
    /// <summary>Files the fight on screen in the recent fights when the meter resets or closes
    /// (flushAll); the 5-second timer's call does nothing - a fight is only over at a reset.</summary>
    private void RecordFinishedFights(bool flushAll)
    {
        if (!flushAll || _historyMode || !_recordFightHistory || _fightStore is null || _aggregator.Events.Count == 0)
        {
            return;
        }

        try
        {
            string title = OverlayTargetText.Text is { Length: > 0 } shown ? shown : LocalizationManager.Instance["Main.FilterAllTargets"];
            var directory = _source?.Entities as Aion2.Aion2EntityDirectory;
            if (RecentFights.Describe(_aggregator.Events.ToList(), title, BuildFightContext(), IsInScope, IsTeammate,
                    id => directory?.SummonOwnerOf(id) is not null) is FightDetail fight)
            {
                RecentFights.Save(_fightStore, fight);
                _fightHistoryWindow?.Refresh();
            }
        }
        catch (Exception ex)
        {
            ShowUploadStatus($"Fight history: {ex.Message}");
        }
    }
```

- [ ] **Step 3: No more loading a fight into the meter** - in `OnFightHistoryClicked` delete the line `_fightHistoryWindow.LoadRequested += EnterHistoryMode;` and add `Topmost = Topmost` to the window's initializer: `_fightHistoryWindow = new FightHistoryWindow(_fightStore) { Owner = this, Topmost = Topmost };`. Delete the whole `EnterHistoryMode` method and its doc comment (its only caller is gone). Keep `ExitHistoryMode` (the banner's handler still names it).

- [ ] **Step 4: Build** - expected: build succeeds once Task 4 removes `LoadRequested` from the window; if this task is built alone, the event still exists and the build succeeds too.

- [ ] **Step 5: Replay check through the headless window** - with the scratch harness `histwin` (data folder pointed at a scratch folder, SQLite initialised, `aion2` capture replayed, `ClearDamageData` at the end): run it on `capture_2026-10-04_10-37-44.jsonl`. Expected: entries are whole overlay fights (trash packs, then one entry for the double boss with about 1.2 M damage), at most 10, no per-monster pieces.

- [ ] **Step 6: Commit**

```bash
git add Client/Ui/MainWindow.xaml.cs
git commit -m "History: the meter files every overlay fight (recent-fights.db) instead of one entry per monster"
```

### Task 4: History button and window

**Files:**
- Modify: `Client/Ui/MainWindow.xaml` (overlay header, after the settings `Border`)
- Modify: `Client/Ui/MainWindow.xaml.cs` (new `OnOverlayHistoryClicked`)
- Modify: `Client/Ui/FightHistoryWindow.xaml`, `Client/Ui/FightHistoryWindow.xaml.cs`
- Modify: `Client/assets/i18n/ui_strings.json`

**Interfaces:**
- Consumes: `FightStore.Query/Load`, `FightDetail.Names/Events/Participants`, `PlayerDetailsWindow(string name, string className, string faction, bool isLocalPlayer, IReadOnlyList<DamageEvent> events, Func<int, string?> nameOf, bool heals = false, bool exactCrits = false, ...)`.

- [ ] **Step 1: Overlay button** - in `MainWindow.xaml`, right after the settings `Border` (`MouseLeftButtonDown="OnOverlaySettingsClicked"`), add:

```xml
                    <Border DockPanel.Dock="Right" Style="{StaticResource OverlayIconButton}" MouseLeftButtonDown="OnOverlayHistoryClicked" ToolTip="{local:Loc Main.Overlay.History}">
                        <TextBlock Text="&#xE81C;" Style="{StaticResource OverlayIconGlyph}"/>
                    </Border>
```

and in `MainWindow.xaml.cs`, next to `OnOverlaySettingsClicked`:

```csharp
    private void OnOverlayHistoryClicked(object sender, MouseButtonEventArgs e)
    {
        OnFightHistoryClicked(sender, e);
        e.Handled = true;
    }
```

- [ ] **Step 2: Slim the window** - in `FightHistoryWindow.xaml`: replace the search `DockPanel` (row 0) with `<TextBlock x:Name="CountText" Grid.Row="0" Margin="0,0,0,6" Foreground="{DynamicResource Brush.TextMuted}"/>`; delete the Server column; give `ParticipantsGrid` `MouseDoubleClick="OnParticipantDoubleClick"`, replace its columns with:

```xml
                <DataGridTextColumn Header="{local:Loc Main.ColumnName}" Binding="{Binding Name}" Width="*"/>
                <DataGridTextColumn Header="{local:Loc History.ColumnClass}" Binding="{Binding ClassName}" Width="100"/>
                <DataGridTextColumn Header="{local:Loc History.ColumnDamage}" Binding="{Binding Damage, StringFormat=N0}" Width="100"/>
                <DataGridTextColumn Header="DPS" Binding="{Binding DpsDisplay}" Width="70"/>
                <DataGridTextColumn Header="%" Binding="{Binding Share}" Width="60"/>
                <DataGridTextColumn Header="{local:Loc History.ColumnHealing}" Binding="{Binding Healing, StringFormat=N0}" Width="90"/>
                <DataGridTextColumn Header="{local:Loc History.ColumnTaken}" Binding="{Binding DamageTaken, StringFormat=N0}" Width="90"/>
```

remove `MouseDoubleClick="OnFightDoubleClick"` from `FightsGrid` and the `LoadButton`; change the window size to `Height="520" Width="720"`.

- [ ] **Step 3: Window code** - replace `FightHistoryWindow.xaml.cs` with:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AionDPS.Combat;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>
/// The last overlay fights (History/RecentFights): pick one to see its players, double-click a
/// player for their skills in that fight. The live overlay keeps running beside it. The store is
/// the caller's - opened once by MainWindow, which files the fights.
/// </summary>
public partial class FightHistoryWindow : Window
{
    private readonly FightStore _store;
    private FightDetail? _shown;

    public FightHistoryWindow(FightStore store)
    {
        InitializeComponent();
        _store = store;
        Refresh();
    }

    public void Refresh()
    {
        List<FightSummary> fights = _store.Query(null, RecentFights.Keep);
        FightsGrid.ItemsSource = fights;
        CountText.Text = string.Format(LocalizationManager.Instance["History.Count"], fights.Count);
        ParticipantsGrid.ItemsSource = null;
        _shown = null;
    }

    /// <summary>One line of the players' grid, with the share of the fight's damage.</summary>
    private sealed record ParticipantRow(string Name, string ClassName, long Damage, string DpsDisplay, string Share, long Healing, long DamageTaken);

    private void OnFightSelected(object sender, SelectionChangedEventArgs e)
    {
        _shown = FightsGrid.SelectedItem is FightSummary summary ? _store.Load(summary.Id) : null;
        long total = _shown?.Participants.Sum(p => p.Damage) ?? 0;
        ParticipantsGrid.ItemsSource = _shown?.Participants
            .Select(p => new ParticipantRow(p.Name, p.ClassName, p.Damage, p.DpsDisplay,
                total > 0 ? (100.0 * p.Damage / total).ToString("F1") + " %" : "", p.Healing, p.DamageTaken))
            .ToList();
    }

    /// <summary>The player's hits in the stored fight, through the same skills window as the
    /// overlay's (ids are the fight's own; names come from its id-to-name table).</summary>
    private void OnParticipantDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_shown is not FightDetail fight || ParticipantsGrid.SelectedItem is not ParticipantRow row)
        {
            return;
        }

        var ids = fight.Names.Where(kv => kv.Value == row.Name).Select(kv => kv.Key).ToHashSet();
        var hits = fight.Events.Where(ev => !ev.IsHeal && ids.Contains(ev.SourceObjectId) && !ids.Contains(ev.TargetObjectId)).ToList();
        FightParticipant? who = fight.Participants.FirstOrDefault(p => p.Name == row.Name);
        new PlayerDetailsWindow(row.Name, row.ClassName, who?.Faction ?? "", who?.IsSelf == true, hits,
            id => fight.Names.GetValueOrDefault(id), exactCrits: true)
        {
            Owner = this,
            Topmost = Topmost,
        }.Show();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 4: Strings** - in `ui_strings.json` (8 languages, same order en, de, fr, es, ru, pl, tr, zh): add `Main.Overlay.History` = Last 10 fights / Letzte 10 Kämpfe / 10 derniers combats / Últimos 10 combates / Последние 10 боёв / Ostatnie 10 walk / Son 10 savaş / 最近 10 场战斗; add `History.Count` = {0} fights / {0} Kämpfe / {0} combats / {0} combates / Боёв: {0} / Walk: {0} / {0} savaş / {0} 场战斗; set `History.Hint` = Double-click a player to see their skills. / Doppelklick auf einen Spieler zeigt seine Fertigkeiten. / Double-cliquez sur un joueur pour voir ses sorts. / Doble clic en un jugador para ver sus habilidades. / Двойной щелчок по игроку — его умения. / Kliknij dwukrotnie gracza, aby zobaczyć jego umiejętności. / Becerilerini görmek için oyuncuya çift tıkla. / 双击玩家查看其技能。; delete `History.Search`, `History.LoadIntoMeter`, `History.ColumnServer` (no other user; check with a search first).

- [ ] **Step 5: Build, self-test, smoke test** - build; self-test `ALL CHECKS PASSED`; smoke harness (real window shown 5 s, settings opened, Ctrl+Alt+H twice) reports `unhandled exceptions: 0`; render the history window from a scratch store holding the replayed 10:37 fights to a PNG and look at it.

- [ ] **Step 6: Commit**

```bash
git add Client/Ui/MainWindow.xaml Client/Ui/MainWindow.xaml.cs Client/Ui/FightHistoryWindow.xaml Client/Ui/FightHistoryWindow.xaml.cs Client/assets/i18n/ui_strings.json
git commit -m "Overlay: history button - the last 10 fights in a window beside the overlay, a player's skills on double-click"
```

### Task 5: Verify on real captures and release

**Files:**
- Modify: `Client/AionDPS.csproj` (`<Version>`)

- [ ] **Step 1: Taken on the arena** - replay `capture_2026-10-04_20-45-35.jsonl` through the headless window in Taken mode (all scopes): the local player's taken includes the opponent's hits (about 84,861 in total over the match) and no self hits.
- [ ] **Step 2: Taken on Thamon** - replay `capture_2026-10-04_00-31-38.jsonl` in Taken mode: no party member appears as an attacker (no "ZZzZ : Attack").
- [ ] **Step 3: Version and release** - set `<Version>0.9.45</Version>`, commit `Version 0.9.45`, push `HEAD:main` and tag `v0.9.45` to the `fork` remote, wait for the release workflow to succeed.
