using AionDPS.History;

namespace AionDPS.Combat;

/// <summary>A caveat the comparison cannot hide; the UI turns each into a sentence.</summary>
public enum DummyWarning
{
    /// <summary>At least one of the fights was not against a training dummy.</summary>
    NotDummyFight,

    /// <summary>The fights differ in length by more than <see cref="DummyLab.DurationTolerance"/>: ramp-up and cooldown rotations weigh differently.</summary>
    DurationsDiffer,

    /// <summary>Too few hits for the averages to mean much.</summary>
    FewHits,

    /// <summary>The DPS moved by less than <see cref="DummyLab.NoiseDpsFraction"/>: run-to-run variance alone can do that.</summary>
    WithinNoise,
}

public enum DummyVerdictKind
{
    /// <summary>A fight has no skill-level snapshot, so levels cannot be compared.</summary>
    NoLevelData,

    /// <summary>No skill level differs: any DPS difference comes from something else (gear, luck, rotation).</summary>
    NoLevelChange,

    /// <summary>Exactly one skill level differs and that skill hit in the fights.</summary>
    SingleChange,

    /// <summary>Exactly one skill level differs but that skill did no damage in either fight.</summary>
    SingleChangeUnused,

    /// <summary>Several skill levels differ: the effect is mixed and cannot be pinned on one.</summary>
    MixedChange,
}

/// <summary>One skill in both fights (A the first, B the second). Levels are null when a fight has no
/// snapshot or does not list the skill. DPS is the skill's damage over the fight's own duration.</summary>
public sealed record DummySkillRow(
    string Skill, int SkillId, int? LevelA, int? LevelB,
    int HitsA, int HitsB, long TotalA, long TotalB,
    double PerCastA, double PerCastB, double CritRateA, double CritRateB,
    double ShareA, double ShareB, double DpsA, double DpsB)
{
    public bool LevelChanged => LevelA is int a && LevelB is int b && a != b;

    /// <summary>What the skill adds to the player's DPS in B compared with A.</summary>
    public double DpsDelta => DpsB - DpsA;

    /// <summary>Damage per cast, B against A (0.2 = 20 % more); null when a side never cast it. Less
    /// noisy than the DPS delta, which also moves with how often the skill was cast.</summary>
    public double? PerCastChange => PerCastA > 0 && PerCastB > 0 ? PerCastB / PerCastA - 1 : null;

    /// <summary>Under <see cref="DummyLab.MinSkillHits"/> casts on at least one side - too few for a per-cast average.</summary>
    public bool LowSample => HitsA < DummyLab.MinSkillHits || HitsB < DummyLab.MinSkillHits;
}

/// <summary>The plain conclusion. <paramref name="Skill"/>, levels, DPS change and per-cast change are
/// set for <see cref="DummyVerdictKind.SingleChange"/>; <paramref name="ChangedCount"/> is how many skill
/// levels differ.</summary>
public sealed record DummyVerdict(DummyVerdictKind Kind, int ChangedCount, string? Skill = null,
    int? LevelA = null, int? LevelB = null, double DpsDelta = 0, double? PerCastChange = null);

public sealed record DummyComparison(
    double SecondsA, double SecondsB, int HitsA, int HitsB, long TotalA, long TotalB, double DpsA, double DpsB,
    IReadOnlyList<DummySkillRow> Rows, IReadOnlyList<DummySkillRow> LevelChanges,
    IReadOnlyList<DummyWarning> Warnings, DummyVerdict Verdict)
{
    public double DpsDelta => DpsB - DpsA;

    /// <summary>B against A as a fraction (0.12 = 12 % more); 0 when A did nothing.</summary>
    public double DpsChange => DpsA > 0 ? DpsB / DpsA - 1 : 0;
}

/// <summary>
/// The dummy lab's maths: two stored fights of the local player (typically against a training dummy,
/// one skill level changed in between) side by side. Per skill: hits, damage, damage per cast, crit
/// rate, share of the total and DPS contribution; the level of each skill in each fight; and a ranking
/// of the skills whose level differs by what their DPS did. Pure - no UI, no store - so SelfCheckHistory
/// covers it. It is honest about noise: a single fight pair is a sample, not a proof, and with more
/// than one level changed nothing is attributed to any of them.
/// SkeeveAN's timed dummy test (History/DummyTest.cs, MIT, upstream SkeeveAN/Aion-DPS-Meter) records a
/// DummyRun (Events, Names, PlayerId, Length) without skill levels; porting it means giving that run a
/// SkillLevels list like <see cref="FightDetail"/> has and feeding its events through <see cref="Compare"/>'s
/// SelfUsage the same way. Nothing of his code is used here.
/// </summary>
public static class DummyLab
{
    /// <summary>Durations further apart than this (a fraction of the longer one) make the DPS figures not quite comparable.</summary>
    public const double DurationTolerance = 0.10;

    /// <summary>A DPS change smaller than this (a fraction of A's) is inside what two identical runs can differ by.</summary>
    public const double NoiseDpsFraction = 0.10;

    public const int MinTotalHits = 30;

    public const int MinSkillHits = 10;

    /// <param name="skillName">Display name of a base skill id, used only in the verdict of a skill that did no damage; null prints "#id".</param>
    public static DummyComparison Compare(FightDetail a, FightDetail b, Func<int, string?>? skillName = null)
    {
        List<SkillUsage> usageA = SelfUsage(a);
        List<SkillUsage> usageB = SelfUsage(b);
        double secondsA = Math.Max(1, a.Summary.Duration.TotalSeconds);
        double secondsB = Math.Max(1, b.Summary.Duration.TotalSeconds);
        long totalA = usageA.Sum(u => u.Total);
        long totalB = usageB.Sum(u => u.Total);
        Dictionary<int, int>? levelsA = LevelMap(a);
        Dictionary<int, int>? levelsB = LevelMap(b);

        var rows = new List<DummySkillRow>();
        foreach (string skill in usageA.Select(u => u.Skill).Union(usageB.Select(u => u.Skill)))
        {
            SkillUsage? ua = usageA.FirstOrDefault(u => u.Skill == skill);
            SkillUsage? ub = usageB.FirstOrDefault(u => u.Skill == skill);
            int id = ua is { SkillId: not 0 } ? ua.SkillId : ub?.SkillId ?? 0;
            rows.Add(new DummySkillRow(skill, id, LevelOf(levelsA, id), LevelOf(levelsB, id),
                ua?.Hits ?? 0, ub?.Hits ?? 0, ua?.Total ?? 0, ub?.Total ?? 0,
                PerCast(ua), PerCast(ub), CritRate(ua), CritRate(ub),
                totalA > 0 ? (ua?.Total ?? 0) / (double)totalA : 0, totalB > 0 ? (ub?.Total ?? 0) / (double)totalB : 0,
                (ua?.Total ?? 0) / secondsA, (ub?.Total ?? 0) / secondsB));
        }

        rows = rows.OrderByDescending(r => Math.Max(r.TotalA, r.TotalB)).ToList();
        List<DummySkillRow> levelChanges = rows.Where(r => r.LevelChanged).OrderByDescending(r => Math.Abs(r.DpsDelta)).ToList();
        int hitsA = usageA.Sum(u => u.Hits);
        int hitsB = usageB.Sum(u => u.Hits);
        double dpsA = totalA / secondsA;
        double dpsB = totalB / secondsB;

        DummyVerdict verdict = BuildVerdict(rows, levelsA, levelsB, secondsA, secondsB, skillName);
        var warnings = new List<DummyWarning>();
        if (!a.Summary.IsDummy || !b.Summary.IsDummy)
        {
            warnings.Add(DummyWarning.NotDummyFight);
        }

        if (Math.Abs(secondsA - secondsB) / Math.Max(secondsA, secondsB) > DurationTolerance)
        {
            warnings.Add(DummyWarning.DurationsDiffer);
        }

        if (hitsA < MinTotalHits || hitsB < MinTotalHits || levelChanges.Any(r => r.LowSample))
        {
            warnings.Add(DummyWarning.FewHits);
        }

        if (dpsA > 0 && Math.Abs(dpsB / dpsA - 1) < NoiseDpsFraction)
        {
            warnings.Add(DummyWarning.WithinNoise);
        }

        return new DummyComparison(secondsA, secondsB, hitsA, hitsB, totalA, totalB, dpsA, dpsB, rows, levelChanges, warnings, verdict);
    }

    /// <summary>The skill levels both fights list and that differ, by the base skill id (the ids ending in 0000, as the
    /// Character window lists them; a skill's variants 16040010... share its level).</summary>
    private static DummyVerdict BuildVerdict(List<DummySkillRow> rows, Dictionary<int, int>? levelsA, Dictionary<int, int>? levelsB,
        double secondsA, double secondsB, Func<int, string?>? skillName)
    {
        if (levelsA is null || levelsB is null)
        {
            return new DummyVerdict(DummyVerdictKind.NoLevelData, 0);
        }

        List<int> changed = levelsA.Keys
            .Where(id => id % 10000 == 0 && levelsB.TryGetValue(id, out int other) && other != levelsA[id])
            .OrderBy(id => id)
            .ToList();
        if (changed.Count == 0)
        {
            return new DummyVerdict(DummyVerdictKind.NoLevelChange, 0);
        }

        if (changed.Count > 1)
        {
            return new DummyVerdict(DummyVerdictKind.MixedChange, changed.Count);
        }

        int root = changed[0];
        var mine = rows.Where(r => r.SkillId != 0 && BaseOf(r.SkillId) == root).ToList();
        if (mine.Count == 0)
        {
            return new DummyVerdict(DummyVerdictKind.SingleChangeUnused, 1, skillName?.Invoke(root) ?? "#" + root, levelsA[root], levelsB[root]);
        }

        long totalA = mine.Sum(r => r.TotalA);
        long totalB = mine.Sum(r => r.TotalB);
        int hitsA = mine.Sum(r => r.HitsA);
        int hitsB = mine.Sum(r => r.HitsB);
        double? perCast = hitsA > 0 && hitsB > 0 && totalA > 0 ? (totalB / (double)hitsB) / (totalA / (double)hitsA) - 1 : null;
        return new DummyVerdict(DummyVerdictKind.SingleChange, 1, mine[0].Skill, levelsA[root], levelsB[root],
            totalB / secondsB - totalA / secondsA, perCast);
    }

    /// <summary>The local player's hits on anything but themselves, by skill - the same grouping as the Player Details window.</summary>
    private static List<SkillUsage> SelfUsage(FightDetail fight)
    {
        string? self = fight.Summary.SelfName ?? fight.Participants.FirstOrDefault(p => p.IsSelf)?.Name;
        if (self is null)
        {
            return new List<SkillUsage>();
        }

        var ids = fight.Names.Where(kv => kv.Value == self).Select(kv => kv.Key).ToHashSet();
        var hits = fight.Events.Where(e => !e.IsHeal && ids.Contains(e.SourceObjectId) && !ids.Contains(e.TargetObjectId)).ToList();
        return SkillBreakdown.For(hits);
    }

    private static Dictionary<int, int>? LevelMap(FightDetail fight)
    {
        if (fight.SkillLevels is not { Count: > 0 } levels)
        {
            return null;
        }

        var map = new Dictionary<int, int>();
        foreach (SkillLevel level in levels)
        {
            map[level.SkillId] = level.Level;
        }

        return map;
    }

    /// <summary>The skill's own entry, else its base skill's (a hit carries 16040010, the login list 16040000).</summary>
    private static int? LevelOf(Dictionary<int, int>? levels, int skillId)
    {
        if (levels is null || skillId == 0)
        {
            return null;
        }

        if (levels.TryGetValue(skillId, out int exact))
        {
            return exact;
        }

        return levels.TryGetValue(BaseOf(skillId), out int byBase) ? byBase : null;
    }

    private static int BaseOf(int skillId) => skillId / 10000 * 10000;

    private static double PerCast(SkillUsage? u) => u is { Hits: > 0 } ? u.Total / (double)u.Hits : 0;

    private static double CritRate(SkillUsage? u) => u is { Hits: > 0 } ? u.CritHits / (double)u.Hits : 0;
}
