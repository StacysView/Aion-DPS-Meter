using System.IO;
using AionDPS.Combat;

namespace AionDPS.History;

/// <summary>
/// The last few overlay fights - everything between two resets of the meter, as the overlay showed
/// it - kept in recent-fights.db. FightRecorder filed one entry per monster instead: a 10:37
/// capture (2026-10-04) gave 16 pieces of trash and two bosses split in parts.
/// </summary>
public static class RecentFights
{
    public const int Keep = 10;

    public static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(10);

    public static string DefaultPath => Path.Combine(AppDataFolder.Path, "recent-fights.db");

    /// <summary>
    /// The fight as a history entry, or null when its damage spans less than <see cref="MinDuration"/>.
    /// Damage is the players' hits on anything but themselves and their own side; DPS runs over the
    /// span from the first to the last of them. <paramref name="isShown"/> is the overlay's scope
    /// (only the players it listed), <paramref name="isTeammate"/> the local player's group (see
    /// <see cref="TakenHits"/>), <paramref name="isSummon"/> leaves heals on summons out, as Heal
    /// mode does.
    /// </summary>
    public static FightDetail? Describe(IReadOnlyList<DamageEvent> events, string title, FightContext context,
        Func<int, bool> isShown, Func<int, bool> isTeammate, Func<int, bool> isSummon)
    {
        var damage = events.Where(ev => !ev.IsHeal && context.IsPlayer(ev.SourceObjectId) && ev.TargetObjectId != ev.SourceObjectId
                && !(context.IsPlayer(ev.TargetObjectId) && isTeammate(ev.TargetObjectId) && isTeammate(ev.SourceObjectId)))
            .ToList();
        if (damage.Count == 0)
        {
            return null;
        }

        DateTime start = damage.Min(ev => ev.Timestamp);
        DateTime end = damage.Max(ev => ev.Timestamp);
        if (end - start < MinDuration)
        {
            return null;
        }

        double seconds = (end - start).TotalSeconds;
        var taken = events.Where(ev => TakenHits.IsHostile(ev, context.IsPlayer, isTeammate)).ToList();
        var heals = events.Where(ev => ev.IsHeal && context.IsPlayer(ev.SourceObjectId) && !isSummon(ev.TargetObjectId)).ToList();
        var participants = damage.Select(ev => ev.SourceObjectId)
            .Concat(heals.Select(ev => ev.SourceObjectId))
            .Concat(taken.Select(ev => ev.TargetObjectId))
            .Distinct()
            .Where(isShown)
            .Select(id =>
            {
                long dealt = damage.Where(ev => ev.SourceObjectId == id).Sum(ev => ev.Amount);
                return new FightParticipant(context.NameOf(id), context.ClassOf(id), context.FactionOf(id), context.IsSelf(id),
                    context.IsEnemy(id), dealt, dealt / seconds,
                    heals.Where(ev => ev.SourceObjectId == id).Sum(ev => ev.Amount),
                    taken.Where(ev => ev.TargetObjectId == id).Sum(ev => ev.Amount));
            })
            .OrderByDescending(p => p.Damage)
            .ToList();

        var names = events.SelectMany(ev => new[] { ev.SourceObjectId, ev.TargetObjectId })
            .Distinct()
            .ToDictionary(id => id, context.NameOf);
        var summary = new FightSummary(0, context.Game, context.ServerName, start, end, title, "overlay",
            participants.Sum(p => p.Damage), participants.Count, participants.FirstOrDefault(p => p.IsSelf)?.Name);
        return new FightDetail(summary, participants, events.OrderBy(ev => ev.Timestamp).ToList(), names);
    }

    /// <summary>Files the fight and keeps the newest <see cref="Keep"/>.</summary>
    public static void Save(FightStore store, FightDetail detail)
    {
        store.Insert(detail);
        store.Prune(retentionDays: 36_500, maxFights: Keep);
    }
}
