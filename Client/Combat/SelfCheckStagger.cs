using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>Who took a monster's stagger bar down (Stacy, 2026-11-10): the amount each skill takes off is learned, then credited to whoever hit.</summary>
public static class SelfCheckStagger
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Stagger by player:");
        var t0 = new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);
        const int bar = 99;
        var readings = new List<StaggerReading> { new(t0, bar, 4500, 4500, false) };
        var events = new List<DamageEvent>();
        int current = 4500;
        int k = 0;
        void Step(params (int Player, string Skill)[] hits)
        {
            k++;
            DateTime at = t0.AddMilliseconds(k * 100);
            foreach ((int player, string skill) in hits)
            {
                events.Add(new DamageEvent(at.AddMilliseconds(-50), player, bar, 1000, false, skill));
                current -= skill switch { "A" => 5, "B" => 10, "C" => 7, _ => 0 };
            }

            readings.Add(new StaggerReading(at, bar, current, 4500, false));
        }

        // A takes 5 and B takes 10 (each seen alone, three times); C is only ever seen with A - then it must come out as 7.
        for (int i = 0; i < 3; i++) { Step((1, "A")); Step((1, "B")); }
        Step((1, "A"), (2, "C"));
        Step((1, "A"), (2, "C"));
        var values = StaggerAttribution.Learn(readings, events);
        bool learned = values.GetValueOrDefault("A") == 5 && values.GetValueOrDefault("B") == 10 && values.GetValueOrDefault("C") == 7;
        Console.WriteLine($"  -> the amounts are learned, alone or with a skill already known (A 5, B 10, C 7): {learned}");

        var credit = StaggerAttribution.Attribute(readings, events);
        StaggerSummary? one = StaggerAttribution.SummaryOf(credit, 1, _ => true);
        StaggerSummary? two = StaggerAttribution.SummaryOf(credit, 2, _ => true);
        bool sums = one is { Player: 55, Team: 69 } && two is { Player: 14 } && one.PerSkill["A"] == 25 && one.PerSkill["B"] == 30;
        Console.WriteLine($"  -> each hit is credited its skill's amount (player 1: 55 of 69, player 2: 14): {sums}");

        StaggerSummary? onlyOne = StaggerAttribution.SummaryOf(credit, 1, id => id == 1);
        bool team = onlyOne is { Team: 55 } && StaggerAttribution.SummaryOf(new Dictionary<int, Dictionary<string, long>>(), 1, _ => true) is null;
        Console.WriteLine($"  -> the team is whoever is counted; no stagger bar read, no summary: {team}");

        var span = ((DateTime, DateTime)?)(t0, t0.AddMilliseconds(300));
        long early = StaggerAttribution.Attribute(readings, events, span).Values.Sum(v => v.Values.Sum());
        Console.WriteLine($"  -> a fight's span keeps only its readings (3 steps: A 5 + B 10 + A 5 = 20): {early == 20}");
        return learned && sums && team && early == 20;
    }
}
