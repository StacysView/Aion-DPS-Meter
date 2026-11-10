namespace AionDPS.Combat;

/// <summary>The tab chosen by the place: Abyss PvP, Ludra Raid from its first boss, Group on leaving (Stacy, 2026-11-10).</summary>
public static class SelfCheckAutoScope
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Automatic tab by place:");
        DateTime z1 = new(2026, 11, 10, 15, 0, 0), z2 = z1.AddMinutes(30), z3 = z2.AddMinutes(30);

        // Abyss: PvP at once, once.
        var step = AutoScope.Next(null, z1, 22, false);
        bool abyss = step.Apply == AutoScopeKind.Pvp && step.AppliedZone == z1;
        abyss &= AutoScope.Next(step.AppliedZone, z1, 22, false).Apply is null;
        // Leaving it: Group, and nothing more afterwards.
        var left = AutoScope.Next(step.AppliedZone, z2, 1010, false);
        abyss &= left.Apply == AutoScopeKind.Group && left.AppliedZone is null;
        abyss &= AutoScope.Next(left.AppliedZone, z2, 1010, false).Apply is null;

        // Ludra: nothing until the first boss is there, then Raid once, Group on leaving.
        bool ludra = AutoScope.Next(null, z2, 5000, false).Apply is null;
        var raid = AutoScope.Next(null, z2, 5000, true);
        ludra &= raid.Apply == AutoScopeKind.Raid && raid.AppliedZone == z2;
        ludra &= AutoScope.Next(raid.AppliedZone, z2, 5000, true).Apply is null; // a tab picked by hand stays
        ludra &= AutoScope.Next(raid.AppliedZone, z3, 1010, false).Apply == AutoScopeKind.Group;
        // Ludra straight into the Abyss: PvP, not Group in between.
        ludra &= AutoScope.Next(raid.AppliedZone, z3, 20, false).Apply == AutoScopeKind.Pvp;

        // Elsewhere nothing happens by itself.
        bool quiet = AutoScope.Next(null, z1, 1010, false).Apply is null && AutoScope.Next(null, z1, 1010, false).AppliedZone is null;

        Console.WriteLine($"  -> Abyss: PvP once then Group on leaving: {abyss}; Ludra: Raid from the first boss, once, Group on leaving: {ludra}; elsewhere nothing: {quiet}");
        return abyss && ludra && quiet;
    }
}
