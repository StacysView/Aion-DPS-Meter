namespace AionDPS.Combat;

/// <summary>
/// One damage or heal instance, already decoded from whatever produced it. Deliberately
/// source-agnostic: the DPS/iDPS math is built and verified against synthetic events, and this
/// type is what let the meter switch from decoded packets to parsed Chat.log lines without the
/// calculator or the aggregator changing at all.
/// </summary>
/// <param name="Skill">The ability used, when the line named one. Null for an auto-attack, and
/// also for the several line shapes that carry a number but no skill. Optional so every existing
/// construction site -- demo data, selftests -- keeps compiling and simply reports no skill.</param>
/// <param name="IsCritical">Whether the client marked the line as a critical hit. Reliable for the
/// LOCAL player only: measured across four logs of one fight, an observer's client marks roughly
/// half of another player's crits (8,8% against the 19,0% that player's own client recorded), so a
/// crit rate shown for anyone else is a floor, not a rate.</param>
/// <param name="IsTick">A damage-over-time tick rather than a hit: it adds to the damage, but not to
/// the hit count or crit rate (the Aion 2 combat analyzer counts a DoT skill's casts as its hits).</param>
/// <param name="Kind">Crit, double, perfect and parry flags of the hit; <see cref="HitKind.None"/> for a plain hit
/// and for events from a source that does not read them.</param>
public readonly record struct DamageEvent(
    DateTime Timestamp,
    int SourceObjectId,
    int TargetObjectId,
    long Amount,
    bool IsHeal,
    string? Skill = null,
    bool IsCritical = false,
    bool IsTick = false,
    int SkillId = 0,
    HitKind Kind = HitKind.None);

/// <summary>What a damage hit's own data says about it besides the number. Crit is also in
/// <see cref="DamageEvent.IsCritical"/>; the others come from the hit frame's extra flags (measured
/// on 170,000 hits: double about x2.0, perfect about x1.1, parry about x0.4). A hit with no flag is
/// a "plain hit", the only kind a skill-level comparison averages.</summary>
[Flags]
public enum HitKind
{
    None = 0,
    Crit = 1,
    Double = 2,
    Perfect = 4,
    Parry = 8,
}
