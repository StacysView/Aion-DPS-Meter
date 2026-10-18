using AionDPS.Aion2;

namespace AionDPS.Schedule;

/// <summary>One watched boss in the overlay's footer: its short name, where it stands, and the time left
/// before it comes (Respawn and Fixed only).</summary>
public readonly record struct BossFooterEntry(string ShortName, BossPhase Phase, TimeSpan? Left);

/// <summary>A reminder about a watched boss: <see cref="Back"/> false while it is 5 minutes or less away,
/// true for 2 minutes from its return. The same boss, respawn and kind are the same alert.</summary>
public readonly record struct BossAlert(int Npc, DateTime Respawn, bool Back);

/// <summary>The watched field bosses (a setting) as the overlay's footer and the mid-screen reminder show them.</summary>
public static class FieldBossTimers
{
    public static readonly TimeSpan ReminderLead = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan BackFor = TimeSpan.FromMinutes(2);

    public static IReadOnlyList<BossFooterEntry> Footer(IEnumerable<int> watched, DateTime now, string language)
    {
        var list = new List<BossFooterEntry>();
        foreach (int npc in watched)
        {
            if (Find(npc) is not { } boss)
            {
                continue;
            }

            BossStatus status = Aion2FieldBosses.StatusOf(boss, now);
            TimeSpan? left = status.Phase is BossPhase.Respawn or BossPhase.Fixed ? status.Remaining(now) : null;
            list.Add(new BossFooterEntry(ShortName(boss, language), status.Phase, left));
        }

        // Only the next one (Stacy, 2026-10-18): a boss that is there first, then the soonest back, then an unknown one;
        // a tie keeps the order watched.
        return list
            .Select((entry, index) => (entry, index))
            .OrderBy(e => e.entry.Phase == BossPhase.Now ? TimeSpan.Zero : e.entry.Left ?? TimeSpan.MaxValue)
            .ThenBy(e => e.index)
            .Take(1)
            .Select(e => e.entry)
            .ToList();
    }

    /// <summary>Every reminder that is due now, in the order watched (two bosses can come back close together).</summary>
    public static IReadOnlyList<BossAlert> Reminders(IEnumerable<int> watched, DateTime now)
    {
        var alerts = new List<BossAlert>();
        foreach (int npc in watched)
        {
            if (Find(npc) is not { } boss)
            {
                continue;
            }

            BossStatus status = Aion2FieldBosses.StatusOf(boss, now);
            if (status.At is not { } at)
            {
                continue;
            }

            if (status.Phase is BossPhase.Respawn or BossPhase.Fixed && at - now <= ReminderLead)
            {
                alerts.Add(new BossAlert(npc, at, false));
                continue;
            }

            if (status.Phase == BossPhase.Now && now >= at && now - at < BackFor)
            {
                alerts.Add(new BossAlert(npc, at, true));
            }
        }

        return alerts;
    }

    /// <summary>"Gartua de l'éternité" and "Eternal Gartua" both become "Gartua": the first word in French, the last in the other languages.</summary>
    public static string ShortName(FieldBoss boss, string language)
    {
        string[] words = boss.NameIn(language).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? boss.NameIn(language) : language == "fr" ? words[0] : words[^1];
    }

    internal static FieldBoss? Find(int npc) =>
        Aion2FieldBosses.Maps.SelectMany(m => m.Bosses).FirstOrDefault(b => b.Npc == npc);
}
