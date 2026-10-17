namespace AionDPS.Combat;

/// <summary>What a line of the boss-death banner is about.</summary>
public enum TeaseKind
{
    /// <summary>The group's best DPS - shown only when someone is teased.</summary>
    Mvp,

    /// <summary>Died <see cref="LowDpsTease.DeathsToTease"/> times or more during the fight.</summary>
    Deaths,

    /// <summary>Far behind the group's best.</summary>
    LowDps,
}

/// <summary>One line of the banner: who, why, their DPS over the fight and their deaths in it.</summary>
public readonly record struct TeaseLine(int PlayerId, TeaseKind Kind, double Dps, int Deaths);

/// <summary>
/// The banner at a boss's death (Ui/TeaseBanner), decided by Stacy (2026-10-11): one line per
/// group member at most, the telling one - died twice or more: their deaths (a low DPS is no news
/// then); else, under half the average of the two best DPS among the others: their DPS. Healers
/// are judged like everyone. When anyone is teased, the group's best DPS opens the banner as MVP.
/// DPS is the overlay's own number: damage over the boss fight, from the first to the last hit on
/// the boss (see <see cref="BossFight.Dps"/>). Never solo, never on a fight under 20 s. Kept apart
/// from the window so the rules can be checked on their own (see SelfCheck).
/// </summary>
public static class LowDpsTease
{
    /// <summary>A shorter fight says nothing about anyone's DPS (a few seconds on a weak boss).</summary>
    public static readonly TimeSpan MinimumFight = TimeSpan.FromSeconds(20);

    /// <summary>Teased when under this share of the average of the two best DPS among the others.</summary>
    public const double ShareOfBest = 0.5;

    /// <summary>A few deaths happen to anyone on mechanics; from four, it is worth a line (two until 2026-10-17, Stacy).</summary>
    public const int DeathsToTease = 4;

    /// <summary>Halloween - its phrases (Tease.Halloween, Tease.HalloweenDeaths), its sounds and the
    /// overlay's look - from 20 October to 4 November, every year (Stacy, 2026-10-15; first 24
    /// October to 2 November).</summary>
    public static bool IsHalloween(DateTime day) => day.Month == 10 && day.Day >= 20 || day.Month == 11 && day.Day <= 4;

    /// <param name="fight">The boss fight's damage (the boss and its adds, see <see cref="BossFight.ShownHits"/>).</param>
    /// <param name="present">The players there: who dealt damage or healed during the fight.</param>
    /// <param name="inGroup">The local player and the group's members. With fewer than two of them there,
    /// the boss was killed solo, and nothing is shown (it fired alone in the Nightmare Altar, 2026-10-11).</param>
    /// <param name="deathsOf">How many times a player died during the fight.</param>
    /// <returns>The MVP first, then the teased players, lowest DPS first; empty when nobody is teased.</returns>
    public static IReadOnlyList<TeaseLine> Pick(IReadOnlyList<DamageEvent> fight, int bossId,
        IEnumerable<int> present, Func<int, bool> inGroup, Func<int, int> deathsOf)
    {
        var damage = fight.Where(ev => !ev.IsHeal).ToList();
        var onBoss = damage.Where(ev => ev.TargetObjectId == bossId).ToList();
        var members = present.Distinct().Where(inGroup).ToList();
        if (onBoss.Count < 2 || onBoss.Max(ev => ev.Timestamp) - onBoss.Min(ev => ev.Timestamp) < MinimumFight
            || members.Count < 2)
        {
            return Array.Empty<TeaseLine>();
        }

        var dps = members.ToDictionary(id => id, id => BossFight.Dps(damage, bossId, id) ?? 0);
        var teased = new List<TeaseLine>();
        foreach (int id in members)
        {
            int deaths = deathsOf(id);
            if (deaths >= DeathsToTease)
            {
                teased.Add(new TeaseLine(id, TeaseKind.Deaths, dps[id], deaths));
                continue;
            }

            double best = dps.Where(kv => kv.Key != id).Select(kv => kv.Value).OrderByDescending(v => v).Take(2).Average();
            if (dps[id] < ShareOfBest * best)
            {
                teased.Add(new TeaseLine(id, TeaseKind.LowDps, dps[id], deaths));
            }
        }

        if (teased.Count == 0)
        {
            return Array.Empty<TeaseLine>();
        }

        int mvp = dps.OrderByDescending(kv => kv.Value).First().Key;
        return teased.OrderBy(line => line.Dps)
            .Prepend(new TeaseLine(mvp, TeaseKind.Mvp, dps[mvp], deathsOf(mvp)))
            .ToList();
    }
}

/// <summary>
/// Phrases drawn like cards from a shuffled deck: none comes back before every other one was used,
/// and a new deck never starts with the phrase that ended the last one.
/// </summary>
public sealed class PhraseDeck
{
    private readonly int _count;
    private readonly Random _random;
    private readonly Queue<int> _left = new();
    private int _last = -1;

    public PhraseDeck(int count, Random? random = null)
    {
        _count = count;
        _random = random ?? new Random();
    }

    /// <summary>The index of the next phrase, 0 to count - 1.</summary>
    public int Draw()
    {
        if (_left.Count == 0)
        {
            int[] order = Enumerable.Range(0, _count).ToArray();
            _random.Shuffle(order);
            if (_count > 1 && order[0] == _last)
            {
                (order[0], order[^1]) = (order[^1], order[0]);
            }

            foreach (int index in order)
            {
                _left.Enqueue(index);
            }
        }

        _last = _left.Dequeue();
        return _last;
    }
}

/// <summary>
/// Jokes on the pseudos of Stacy's group (Stacy, 2026-10-15: Boulenbouche, Azaède, Kayzia, bobow),
/// in assets/tease/personal.json - public, in the repository, by his choice. Each one is for that
/// player alone ({0} the name, {1} the DPS); a name is matched whatever its case. No file, or a
/// broken one, means no such jokes, nothing else.
/// </summary>
public static class PersonalTeases
{
    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? _all;

    public static IReadOnlyList<string> For(string name) =>
        (_all ??= Load(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "tease", "personal.json")))
            .TryGetValue(name.Trim(), out var jokes) ? jokes : Array.Empty<string>();

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Load(string path)
    {
        var jokes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            foreach (var player in doc.RootElement.GetProperty("players").EnumerateObject())
            {
                jokes[player.Name] = player.Value.EnumerateArray().Select(j => j.GetString() ?? "").Where(j => j.Length > 0).ToList();
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Nothing personal then.
        }

        return jokes;
    }
}
