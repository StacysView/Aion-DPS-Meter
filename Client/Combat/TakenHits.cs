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
