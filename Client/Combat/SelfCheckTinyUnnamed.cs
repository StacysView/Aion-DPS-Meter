namespace AionDPS.Combat;

/// <summary>The unnamed players under one percent of the damage are left out of the list (Stacy, 2026-11-10).</summary>
public static class SelfCheckTinyUnnamed
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Tiny unnamed players:");
        var damage = new Dictionary<int, long> { [1] = 5_000_000, [2] = 3_000_000, [3] = 700, [4] = 60_000, [5] = 40 };
        // 3 and 5 are unnamed and tiny; 4 is unnamed but over one percent (60,000 of 8,060,740 is 0.74 % - so it goes too); 2 is a real, named player
        var unnamed = new HashSet<int> { 3, 4, 5 };
        var kept = TinyUnnamed.Keep(new[] { 1, 2, 3, 4, 5 }, id => damage[id], unnamed.Contains);
        bool small = kept.SequenceEqual(new[] { 1, 2 });

        damage[4] = 200_000; // 2.4 %: an unnamed player who counts stays
        var kept2 = TinyUnnamed.Keep(new[] { 1, 2, 3, 4, 5 }, id => damage[id], unnamed.Contains);
        bool big = kept2.SequenceEqual(new[] { 1, 2, 4 });

        bool tinyNamed = TinyUnnamed.Keep(new[] { 1, 6 }, id => id == 1 ? 5_000_000 : 10, _ => false).Count == 2; // a named player never goes, however small
        bool nothing = TinyUnnamed.Keep(new[] { 3 }, _ => 0, _ => true).Count == 1; // no damage at all: nothing to compare against
        Console.WriteLine($"  -> unnamed and under 1 % go, unnamed over 1 % stay, named stay, empty lists untouched: {small && big && tinyNamed && nothing}");
        return small && big && tinyNamed && nothing;
    }
}
