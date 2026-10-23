using System.IO;
using AionDPS.History;

namespace AionDPS.Combat;

/// <summary>Fight history: segmentation by silence gap, recording only finished fights, and a
/// SQLite round trip (insert, search, load with events intact, prune).</summary>
public static class SelfCheckHistory
{
    public static bool Run()
    {
        bool ok = true;
        ok &= RunSegmenterScenario();
        ok &= RunRecorderAndStoreScenario();
        ok &= RunTakenHitsScenario();
        ok &= RunRecentFightsScenario();
        ok &= RunEventBlobScenario();
        ok &= RunDummyLabScenario();
        ok &= RunSkillLevelMigrationScenario();
        return ok;
    }

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

    /// <summary>An overlay fight as a history entry (synthetic): the shown players only, damage, DPS
    /// over their own span (a stranger's earlier hits left out), healing, hits taken from a monster
    /// and an outside player; a 5 s poke is no fight; the store keeps the newest ten.</summary>
    private static bool RunRecentFightsScenario()
    {
        Console.WriteLine("[selftest] Recent fights: one overlay fight as a history entry, the newest twenty kept:");
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
        // A stranger hitting the boss long before the shown players' fight does not stretch it.
        events.Add(new DamageEvent(t.AddSeconds(-30), Stranger, Boss, 5000, IsHeal: false));
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
        bool keptNewest;
        using (var store = new FightStore(path))
        {
            for (int i = 0; i < RecentFights.Keep + 2; i++)
            {
                var shifted = events.Select(ev => ev with { Timestamp = ev.Timestamp.AddMinutes(i) }).ToList();
                RecentFights.Save(store, RecentFights.Describe(shifted, $"Fight {i}", context, shown.Contains, shown.Contains, _ => false)!);
            }

            var left = store.Query(null, 50);
            keptNewest = left.Count == RecentFights.Keep && left[0].TargetName == $"Fight {RecentFights.Keep + 1}" && left.All(f => f.TargetName != "Fight 0" && f.TargetName != "Fight 1");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TryDelete(path);
        Console.WriteLine($"  -> shown players with damage/DPS/healing/taken: {described}; a 5 s poke is no fight: {tooShort}; newest twenty kept: {keptNewest}");
        return described && tooShort && keptNewest;
    }

    /// <summary>A stored event keeps its skill id and tick flag - the skills window finds a skill's
    /// icon by its id, and a fight opened from the history had none.</summary>
    private static bool RunEventBlobScenario()
    {
        Console.WriteLine("[selftest] Stored events keep their skill id and tick flag (icons in the history's skills window):");
        var sent = new DamageEvent(new DateTime(2026, 10, 4, 21, 0, 0, DateTimeKind.Local), 1, 100, 1234, IsHeal: false,
            Skill: "Combustion", IsCritical: true, IsTick: true, SkillId: 16040010);
        var (events, _) = EventBlob.Unpack(EventBlob.Pack(new[] { sent }, new Dictionary<int, string>()));
        bool ok = events.Count == 1 && events[0] == sent;
        Console.WriteLine($"  -> the event comes back whole: {ok}");
        return ok;
    }

    private static FightDetail DummyFight(DateTime start, int cleaveDamage, int cleaveLevel, int boltLevel, string target = "Training Scarecrow", int casts = 20)
    {
        const int Me = 1, Dummy = 100;
        var events = new List<DamageEvent>();
        for (int i = 0; i < casts; i++)
        {
            events.Add(new DamageEvent(start.AddSeconds(i), Me, Dummy, cleaveDamage, IsHeal: false, Skill: "Cleave", IsCritical: i < 2, SkillId: 11010010));
            events.Add(new DamageEvent(start.AddSeconds(i), Me, Dummy, 500, IsHeal: false, Skill: "Bolt", SkillId: 12010010));
        }

        long total = events.Sum(e => e.Amount);
        var summary = new FightSummary(0, "aion2", null, start, start.AddSeconds(20), target, "overlay", total, 1, "Me");
        var me = new FightParticipant("Me", "Gladiator", "Elyos", true, false, total, total / 20.0, 0, 0);
        var levels = new List<SkillLevel> { new(11010000, cleaveLevel, 10), new(12010000, boltLevel, 10) };
        return new FightDetail(summary, new[] { me }, events, new Dictionary<int, string> { [Me] = "Me", [Dummy] = target }, levels);
    }

    /// <summary>The dummy lab's maths on two synthetic 20 s fights: per-skill numbers, the level read
    /// from the base skill id, the DPS delta, the ranking, and the warnings and verdicts.</summary>
    private static bool RunDummyLabScenario()
    {
        Console.WriteLine("[selftest] Dummy lab: per-skill comparison, level changes, noise warnings:");
        DateTime t = new(2026, 10, 20, 20, 0, 0);
        FightDetail a = DummyFight(t, 1000, 10, 10);
        FightDetail b = DummyFight(t.AddMinutes(5), 1200, 12, 10);
        DummyComparison one = DummyLab.Compare(a, b);
        DummySkillRow cleave = one.Rows.Single(r => r.Skill == "Cleave");
        bool maths = cleave.HitsA == 20 && cleave.TotalA == 20_000 && cleave.TotalB == 24_000
            && Math.Abs(cleave.PerCastA - 1000) < 0.01 && Math.Abs(cleave.PerCastB - 1200) < 0.01
            && Math.Abs(cleave.CritRateA - 0.1) < 1e-9 && Math.Abs(cleave.ShareA - 2.0 / 3) < 1e-9
            && Math.Abs(cleave.DpsDelta - 200) < 0.01 && Math.Abs((cleave.PerCastChange ?? 0) - 0.2) < 1e-9
            && Math.Abs(one.DpsA - 1500) < 0.01 && Math.Abs(one.DpsDelta - 200) < 0.01;
        bool levels = cleave.LevelA == 10 && cleave.LevelB == 12 && one.LevelChanges.Count == 1 && one.LevelChanges[0].Skill == "Cleave";
        bool single = one.Verdict.Kind == DummyVerdictKind.SingleChange && one.Verdict.Skill == "Cleave" && Math.Abs(one.Verdict.DpsDelta - 200) < 0.01
            && one.Warnings.Count == 0;

        DummyComparison mixed = DummyLab.Compare(a, DummyFight(t.AddMinutes(5), 1200, 12, 11));
        bool mixedVerdict = mixed.Verdict.Kind == DummyVerdictKind.MixedChange && mixed.Verdict.ChangedCount == 2 && mixed.Verdict.Skill is null;
        bool noChange = DummyLab.Compare(a, DummyFight(t.AddMinutes(5), 1000, 10, 10)).Verdict.Kind == DummyVerdictKind.NoLevelChange;
        bool noData = DummyLab.Compare(a, b with { SkillLevels = null }).Verdict.Kind == DummyVerdictKind.NoLevelData;

        DummyComparison shaky = DummyLab.Compare(DummyFight(t, 1000, 10, 10, casts: 5), DummyFight(t, 1050, 12, 10, "Boss", casts: 5));
        bool warned = shaky.Warnings.Contains(DummyWarning.FewHits) && shaky.Warnings.Contains(DummyWarning.NotDummyFight)
            && shaky.Warnings.Contains(DummyWarning.WithinNoise);
        FightDetail longer = b with { Summary = b.Summary with { EndedAt = b.Summary.StartedAt.AddSeconds(30) } };
        bool durations = DummyLab.Compare(a, longer).Warnings.Contains(DummyWarning.DurationsDiffer);
        bool dummies = DummyTargets.IsDummy("Punching Bag_Elyos Tribe") && DummyTargets.IsDummy("melee training scarecrow")
            && DummyTargets.IsDummy("Mannequin d'entraînement") && !DummyTargets.IsDummy("Raksha Boilheart");
        Console.WriteLine($"  -> per cast, crit, share, DPS delta: {maths}; levels from the base id, ranking: {levels}; one change, no warning: {single}");
        Console.WriteLine($"  -> two changes are mixed: {mixedVerdict}; none: {noChange}; no snapshot: {noData}");
        Console.WriteLine($"  -> few hits / not a dummy / within noise: {warned}; durations 20 s vs 30 s: {durations}; dummy names: {dummies}");
        return maths && levels && single && mixedVerdict && noChange && noData && warned && durations && dummies;
    }

    /// <summary>A fights.db written before the skill_levels column opens, loads with no levels, and
    /// from then on stores them; reopening a migrated file does not fail on the existing column.</summary>
    private static bool RunSkillLevelMigrationScenario()
    {
        Console.WriteLine("[selftest] Fight store: skill_levels column added to an older fights.db:");
        string path = Path.Combine(Path.GetTempPath(), $"aiondps-migrate-{Guid.NewGuid():N}.db");
        DateTime t = new(2026, 10, 20, 20, 0, 0);
        using (var old = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            old.Open();
            using var create = old.CreateCommand();
            create.CommandText = """
                CREATE TABLE fights (id INTEGER PRIMARY KEY AUTOINCREMENT, game TEXT NOT NULL, server_name TEXT, started_at TEXT NOT NULL,
                    ended_at TEXT NOT NULL, target_name TEXT NOT NULL, kind TEXT NOT NULL, total_damage INTEGER NOT NULL,
                    participant_count INTEGER NOT NULL, self_name TEXT, events_gz BLOB NOT NULL);
                CREATE TABLE participants (id INTEGER PRIMARY KEY AUTOINCREMENT, fight_id INTEGER NOT NULL REFERENCES fights(id) ON DELETE CASCADE,
                    name TEXT NOT NULL, class_name TEXT NOT NULL, faction TEXT NOT NULL, is_self INTEGER NOT NULL, is_enemy INTEGER NOT NULL,
                    damage INTEGER NOT NULL, dps REAL, healing INTEGER NOT NULL, damage_taken INTEGER NOT NULL);
                INSERT INTO fights (game, started_at, ended_at, target_name, kind, total_damage, participant_count, events_gz)
                VALUES ('aion2', $s, $e, 'Punching Bag', 'overlay', 0, 0, $blob);
                """;
            create.Parameters.AddWithValue("$s", t.ToString("o"));
            create.Parameters.AddWithValue("$e", t.AddSeconds(20).ToString("o"));
            create.Parameters.AddWithValue("$blob", EventBlob.Pack(Array.Empty<DamageEvent>(), new Dictionary<int, string>()));
            create.ExecuteNonQuery();
        }

        bool ok;
        using (var store = new FightStore(path))
        {
            FightDetail? oldFight = store.Load(store.Query(null)[0].Id);
            bool oldLoads = oldFight is not null && oldFight.SkillLevels is null && oldFight.Summary.IsDummy;
            long id = store.Insert(DummyFight(t.AddMinutes(5), 1200, 12, 10));
            FightDetail? back = store.Load(id);
            bool stored = back?.SkillLevels is { Count: 2 } l && l[0] == new SkillLevel(11010000, 12, 10);
            ok = oldLoads && stored;
            Console.WriteLine($"  -> the old row loads without levels: {oldLoads}; a new fight keeps its levels: {stored}");
        }

        using (var again = new FightStore(path))
        {
            bool reopened = again.Count() == 2;
            Console.WriteLine($"  -> reopening the migrated file: {reopened}");
            ok &= reopened;
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TryDelete(path);
        return ok;
    }

    private static void TryDelete(string path)
    {
        foreach (string file in new[] { path, path + "-wal", path + "-shm" })
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file still held open is left for the OS's temp cleanup.
            }
        }
    }

    private const int You = 1;
    private const int Healer = 2;
    private const int Boss = 100;
    private const int Dummy = 101;

    private static List<DamageEvent> TwoRuns(DateTime start)
    {
        var events = new List<DamageEvent>();
        for (int i = 0; i < 30; i++)
        {
            events.Add(new DamageEvent(start.AddSeconds(i * 2), You, Boss, 1000 + i, IsHeal: false, Skill: "Cleave"));
        }
        events.Add(new DamageEvent(start.AddSeconds(20), Boss, You, 500, IsHeal: false));
        events.Add(new DamageEvent(start.AddSeconds(21), Healer, You, 400, IsHeal: true, Skill: "Healing Light"));
        // Ten minutes later: the same boss again - a separate run.
        DateTime second = start.AddMinutes(10);
        for (int i = 0; i < 20; i++)
        {
            events.Add(new DamageEvent(second.AddSeconds(i * 3), You, Boss, 2000, IsHeal: false));
        }
        // A training dummy poke that must never be recorded.
        events.Add(new DamageEvent(start.AddMinutes(20), You, Dummy, 1, IsHeal: false));
        events.Add(new DamageEvent(start.AddMinutes(20).AddSeconds(30), You, Dummy, 1, IsHeal: false));
        return events;
    }

    private static bool RunSegmenterScenario()
    {
        Console.WriteLine("[selftest] Fight segmentation (120 s silence gap):");
        var start = new DateTime(2026, 9, 22, 20, 0, 0);
        List<FightSegment> segments = FightSegmenter.Segment(TwoRuns(start), Boss);
        bool twoRuns = segments.Count == 2;
        bool firstBounds = twoRuns && segments[0].Start == start && segments[0].End == start.AddSeconds(58) && segments[0].Hits.Count == 30;
        bool secondBounds = twoRuns && segments[1].Start == start.AddMinutes(10) && segments[1].Hits.Count == 20;
        bool healsExcluded = twoRuns && segments[0].Hits.All(h => !h.IsHeal && h.TargetObjectId == Boss);
        Console.WriteLine($"  -> two runs, split at the ten-minute gap: {twoRuns}");
        Console.WriteLine($"  -> first run spans its own hits only: {firstBounds}");
        Console.WriteLine($"  -> second run picked up whole: {secondBounds}");
        Console.WriteLine($"  -> heals and the boss's own hits are not part of a run: {healsExcluded}");
        return twoRuns && firstBounds && secondBounds && healsExcluded;
    }

    private static bool RunRecorderAndStoreScenario()
    {
        Console.WriteLine("[selftest] Fight recorder + SQLite store:");
        string path = Path.Combine(Path.GetTempPath(), "aiondps-selftest-" + Guid.NewGuid().ToString("N"), "fights.db");
        try
        {
            using var store = new FightStore(path);
            var recorder = new FightRecorder(store);
            var start = new DateTime(2026, 9, 22, 20, 0, 0);
            List<DamageEvent> events = TwoRuns(start);
            var names = new Dictionary<int, string> { [You] = "You", [Healer] = "Sardine", [Boss] = "Raksha Boilheart", [Dummy] = "Training Dummy" };
            var context = new FightContext(
                NameOf: id => names.GetValueOrDefault(id, $"0x{id:X8}"),
                ClassOf: id => id == You ? "Gladiator" : id == Healer ? "Cleric" : "?",
                FactionOf: _ => "Elyos",
                IsPlayer: id => id is You or Healer,
                IsSelf: id => id == You,
                IsEnemy: _ => false,
                IsIgnoredTarget: name => name == "Training Dummy",
                Game: "aion",
                ServerName: "Origin Aion");

            // Mid-first-fight: nothing has ended yet.
            int duringFirst = recorder.Tick(events.Where(e => e.Timestamp <= start.AddSeconds(30)).ToList(), start.AddSeconds(31), context);
            // Long after the first run, before the second: exactly the first run is filed.
            int afterFirst = recorder.Tick(events.Where(e => e.Timestamp <= start.AddSeconds(58)).ToList(), start.AddMinutes(5), context);
            // Same call again must not file it twice.
            int again = recorder.Tick(events.Where(e => e.Timestamp <= start.AddSeconds(58)).ToList(), start.AddMinutes(6), context);
            // Flush at the end files the second run but never the dummy.
            int flushed = recorder.Tick(events, start.AddMinutes(21), context, flushAll: true);

            bool recordedWhenEnded = duringFirst == 0 && afterFirst == 1 && again == 0 && flushed == 1 && store.Count() == 2;

            List<FightSummary> all = store.Query(null);
            List<FightSummary> bySardine = store.Query("sardine");
            List<FightSummary> byBoss = store.Query("boilheart");
            bool searchWorks = all.Count == 2 && bySardine.Count == 1 && byBoss.Count == 2 && all[0].StartedAt > all[1].StartedAt;

            FightDetail? first = store.Load(bySardine[0].Id);
            bool detailIntact = first is not null
                && first.Summary.TargetName == "Raksha Boilheart"
                && first.Summary.SelfName == "You"
                && first.Participants.Count == 2
                && first.Participants[0].Name == "You" && first.Participants[0].Damage == Enumerable.Range(0, 30).Sum(i => 1000 + i)
                && first.Participants[0].DamageTaken == 500
                && first.Participants.Single(p => p.Name == "Sardine").Healing == 400
                && first.Events.Count == 32
                && first.Events.All(e => e.Timestamp.Kind == DateTimeKind.Unspecified)
                && first.Events.Any(e => e.Skill == "Cleave")
                && first.Names[Boss] == "Raksha Boilheart";

            int pruned = store.Prune(retentionDays: 3650, maxFights: 1);
            bool pruneKeepsNewest = pruned == 1 && store.Count() == 1 && store.Query(null)[0].StartedAt == start.AddMinutes(10);

            Console.WriteLine($"  -> fights are filed once, only after the silence gap or a flush, never the dummy: {recordedWhenEnded}");
            Console.WriteLine($"  -> search by target or participant, newest first: {searchWorks}");
            Console.WriteLine($"  -> loaded detail keeps participants, events, skills and names: {detailIntact}");
            Console.WriteLine($"  -> prune to a cap keeps the newest fight: {pruneKeepsNewest}");
            return recordedWhenEnded && searchWorks && detailIntact && pruneKeepsNewest;
        }
        finally
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
