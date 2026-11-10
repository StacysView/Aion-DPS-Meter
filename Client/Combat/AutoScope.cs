namespace AionDPS.Combat;

/// <summary>The view the meter should take by itself.</summary>
public enum AutoScopeKind
{
    Group,
    Raid,
    Pvp,
}

/// <summary>
/// The overlay's tab chosen by the place (Stacy, 2026-11-10): in the Abyss the PvP tab, in Ludra the Raid tab from its first boss (Central Abyss Fusion Reactor),
/// and the Group tab again on leaving. Once per zone: a tab picked by hand afterwards stays.
/// </summary>
public static class AutoScope
{
    private const string LudraInstance = "Abyssal Forge: Ludra";
    private const string FirstBossName = "Central Abyss Fusion Reactor";

    /// <summary>The lower, middle and upper Abyss (see Aion2WorldMaps.OfZone).</summary>
    public static bool IsAbyssZone(int zoneId) => zoneId is 20 or 22 or 23;

    /// <summary>The first boss of Ludra, Normal or Hard.</summary>
    public static bool IsFirstLudraBoss(int npcId) =>
        Aion2.Protocol.Aion2BossCatalog.Find(npcId) is { } boss && boss.Instance.StartsWith(LudraInstance, StringComparison.Ordinal) && boss.Name == FirstBossName;

    /// <summary>The first boss of Ludra was seen in this zone (its hit points were read since <paramref name="zone"/>).</summary>
    public static bool FirstBossHere(Aion2.Aion2EntityDirectory directory, DateTime zone) =>
        directory.KnownBosses().Any(b => IsFirstLudraBoss(b.NpcId) && directory.HitPoints.Latest(b.EntityId) is { } hp && hp.At >= zone);

    /// <param name="appliedZone">The zone where a tab was chosen by place, null when none is.</param>
    /// <param name="zone">The zone now.</param>
    /// <returns>The tab to switch to (null: leave it) and the zone it was chosen in.</returns>
    public static (AutoScopeKind? Apply, DateTime? AppliedZone) Next(DateTime? appliedZone, DateTime zone, int zoneId, bool firstBossHere)
    {
        AutoScopeKind? wanted = IsAbyssZone(zoneId) ? AutoScopeKind.Pvp : firstBossHere ? AutoScopeKind.Raid : null;
        if (appliedZone is null)
        {
            return wanted is { } first ? (first, zone) : (null, null);
        }

        if (appliedZone == zone)
        {
            return (null, appliedZone);
        }

        return wanted is { } next ? (next, zone) : (AutoScopeKind.Group, null);
    }
}
