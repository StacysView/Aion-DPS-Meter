namespace AionDPS.Combat;

/// <summary>The Ludra run's death table and the share of hits that landed (Stacy, 2026-11-09).</summary>
public static class SelfCheckRunDeaths
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Run deaths and landed hits:");
        var t0 = new DateTime(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc);
        var bosses = new List<RunBoss>
        {
            new(2301006, "Isolation Specialist Rahu", t0, t0.AddMinutes(4)),
            new(2301007, "Abyssal Wings Ketu", t0.AddMinutes(8), t0.AddMinutes(12)),
        };
        // 1: one death at Rahu, two at Ketu, one on the way between (total 4). 2: none. 3: nothing known. 4: one at Ketu.
        int? Count(int player, DateTime from, DateTime to)
        {
            if (player == 3)
            {
                return null;
            }

            var deaths = player switch
            {
                1 => new[] { t0.AddMinutes(2), t0.AddMinutes(9), t0.AddMinutes(10), t0.AddMinutes(6) },
                4 => new[] { t0.AddMinutes(11) },
                _ => Array.Empty<DateTime>(),
            };
            return deaths.Count(d => d >= from && d <= to);
        }

        string Name(int id) => "P" + id;
        bool Took(int player, RunBoss boss) => !(player == 4 && boss.NpcId == 2301006); // 4 only fought Ketu
        var rows = RunDeaths.Build(bosses, new[] { 2, 3, 1, 4, 1 }, Count, Took, Name);
        bool order = rows.Select(r => r.PlayerId).SequenceEqual(new[] { 1, 4, 2, 3 }); // most deaths first, nobody twice, unknown last
        bool counts = rows[0].Total == 4 && rows[0].PerBoss[0] == 1 && rows[0].PerBoss[1] == 2 && rows[1].Total == 1 && rows[2].Total == 0 && rows[3].Total is null && rows[3].PerBoss.All(p => p is null) && rows[1].PerBoss[0] is null && rows[1].PerBoss[1] == 1;
        bool noBoss = RunDeaths.Build(Array.Empty<RunBoss>(), new[] { 1 }, Count, Took, Name).Count == 0;
        Console.WriteLine($"  -> the table: most deaths first, per boss and in all (between two bosses counted in the total), unknown players last: {order && counts && noBoss}");

        bool ludra = RunDeaths.IsLudraBoss(2301006) && RunDeaths.IsLudraBoss(2300290) && !RunDeaths.IsLudraBoss(1)
            && RunDeaths.IsFinalBoss(2301014) && RunDeaths.IsFinalBoss(2300290) && !RunDeaths.IsFinalBoss(2301006);
        Console.WriteLine($"  -> the Ludra bosses (Normal and Hard) are recognised, Eternal Ludra ends the run: {ludra}");

        DamageEvent Hit(HitKind kind = HitKind.None, bool tick = false, bool heal = false) => new(t0, 1, 2, 100, heal, IsTick: tick, Kind: kind);
        var landed = HitRate.Of(new[] { Hit(), Hit(HitKind.Crit), Hit(HitKind.Parry), Hit(HitKind.Parry | HitKind.Double), Hit(tick: true), Hit(heal: true) });
        bool rate = landed.Hits == 4 && landed.Parried == 2 && landed.LandedPercent == 50.0 && HitRate.Of(Array.Empty<DamageEvent>()).LandedPercent is null;
        Console.WriteLine($"  -> landed hits: parried ones counted, ticks and heals not, none without a hit: {rate}");
        return order && counts && noBoss && ludra && rate;
    }
}
