using System.IO;
using System.Text.Json;

namespace AionDPS.Combat;

/// <summary>One Pokémon of the dex: its names, its rarity tier (0 common ... 3 very rare, 4 legendary or mythical) and its evolutions.</summary>
public sealed record DexEntry(int Id, string Fr, string En, int Tier, int[] To, int? From);

/// <summary>The 649 first Pokémon (assets/pokemon/dex.json, built from PokeAPI's data: the tier comes from the species' capture rate).</summary>
public sealed class PokemonDex
{
    private readonly Dictionary<int, DexEntry> _byId;

    public PokemonDex(IEnumerable<DexEntry> entries)
    {
        _byId = entries.ToDictionary(e => e.Id);
        // the first forms: the only ones that appear in the wild
        Bases = _byId.Values.Where(e => e.From is null).OrderBy(e => e.Id).ToList();
    }

    public IReadOnlyList<DexEntry> Bases { get; }

    public int Count => _byId.Count;

    public DexEntry? Get(int id) => _byId.GetValueOrDefault(id);

    /// <summary>The dex of the install folder, or null when its file is missing or broken.</summary>
    public static PokemonDex? Load(string? file = null)
    {
        try
        {
            file ??= Path.Combine(AppContext.BaseDirectory, "assets", "pokemon", "dex.json");
            if (!File.Exists(file))
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            var list = new List<DexEntry>();
            foreach (JsonElement e in doc.RootElement.EnumerateArray())
            {
                int[] to = e.GetProperty("to").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                int? from = e.GetProperty("from").ValueKind == JsonValueKind.Null ? null : e.GetProperty("from").GetInt32();
                list.Add(new DexEntry(e.GetProperty("id").GetInt32(), e.GetProperty("fr").GetString() ?? "", e.GetProperty("en").GetString() ?? "", e.GetProperty("t").GetInt32(), to, from));
            }

            return list.Count > 0 ? new PokemonDex(list) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>A Pokémon on offer today: a first form, shiny or not.</summary>
public sealed record Offer(int Id, bool Shiny);

/// <summary>A Pokémon of the team: <see cref="Path"/> is its line of forms from the first (the branch of a Pokémon with several evolutions is drawn once, when it is caught).</summary>
public sealed class TeamMember
{
    public int[] Path { get; set; } = [];
    public bool Shiny { get; set; }
    public int CaughtLevel { get; set; }
    public int CaughtDay { get; set; }
    public int ShownStage { get; set; }
}

/// <summary>
/// The Monsters theme's little game (Stacy, 2026-11-24): each day four Pokémon appear in the wild, drawn by rarity and different for each player; a click catches one for good (100 %),
/// in one of the four places that open at trainer levels 1, 10, 20 and 30. A caught Pokémon evolves one form every ten levels after the catch (the levels past the thirtieth are steps of
/// <see cref="TrainerXp.BeyondCost"/> of damage). The team can be released from the settings. Kept in a small file of the meter's data folder, with a random seed of the install.
/// </summary>
public sealed class PokemonTeam
{
    /// <summary>The trainer level that opens each of the four places.</summary>
    public static readonly int[] SlotLevels = [1, 10, 20, 30];

    /// <summary>The levels between two evolutions.</summary>
    public const int LevelsPerEvolution = 10;

    /// <summary>The chance, for each Pokémon of the wild, of being shiny: 1 in this many.</summary>
    public const int ShinyOdds = 50;

    private static readonly int[] TierWeights = [62, 22, 12, 2, 2];

    private readonly string? _file;
    private readonly PokemonDex _dex;
    private int _offersDay;
    private List<Offer> _offers = new();

    public PokemonTeam(PokemonDex dex, string? file = null, long seed = 0)
    {
        _dex = dex;
        _file = file;
        Seed = seed != 0 ? seed : Random.Shared.NextInt64(1, long.MaxValue);
    }

    public long Seed { get; private set; }

    public TeamMember?[] Slots { get; } = new TeamMember?[4];

    /// <summary>How many places the level opens (0 to 4).</summary>
    public static int SlotsOpen(int level) => SlotLevels.Count(l => level >= l);

    /// <summary>The level at which the next place opens, or null when all are open.</summary>
    public static int? NextSlotLevel(int level) => SlotLevels.Where(l => level < l).Select(l => (int?)l).FirstOrDefault();

    public static int DayKey(DateTime day) => day.Year * 10000 + day.Month * 100 + day.Day;

    /// <summary>The form (0 first, 1 second, ...) a member has reached at this level.</summary>
    public static int StageAt(TeamMember member, int level) =>
        Math.Clamp((Math.Max(level, member.CaughtLevel) - member.CaughtLevel) / LevelsPerEvolution, 0, Math.Max(0, member.Path.Length - 1));

    public static int IdAt(TeamMember member, int level) => member.Path[StageAt(member, level)];

    private int SeedMix(int salt) => unchecked((int)(Seed ^ (Seed >> 32)) * 31 + salt);

    /// <summary>The four Pokémon of the day: the same all day for this install (and not for another), the first forms only, by rarity; those caught on an earlier day do not come back while they are in the team.</summary>
    public IReadOnlyList<Offer> Today(DateTime day)
    {
        int key = DayKey(day);
        if (_offersDay == key && _offers.Count > 0)
        {
            return _offers;
        }

        var rng = new Random(SeedMix(key));
        var owned = Slots.OfType<TeamMember>().Where(m => m.CaughtDay != key).Select(m => m.Path[0]).ToHashSet();
        var pool = _dex.Bases.Where(e => !owned.Contains(e.Id)).ToList();
        var offers = new List<Offer>();
        int wanted = Math.Min(4, pool.Count);
        while (offers.Count < wanted)
        {
            int tier = PickTier(rng, pool);
            var inTier = pool.Where(e => e.Tier == tier).ToList();
            DexEntry chosen = inTier[rng.Next(inTier.Count)];
            pool.Remove(chosen);
            offers.Add(new Offer(chosen.Id, rng.Next(ShinyOdds) == 0));
        }

        _offersDay = key;
        _offers = offers;
        return _offers;
    }

    private static int PickTier(Random rng, List<DexEntry> pool)
    {
        int total = 0;
        for (int t = 0; t < TierWeights.Length; t++)
        {
            if (pool.Any(e => e.Tier == t))
            {
                total += TierWeights[t];
            }
        }

        int roll = rng.Next(total);
        for (int t = 0; t < TierWeights.Length; t++)
        {
            if (!pool.Any(e => e.Tier == t))
            {
                continue;
            }

            if (roll < TierWeights[t])
            {
                return t;
            }

            roll -= TierWeights[t];
        }

        return pool[0].Tier;
    }

    /// <summary>The first open place with nobody in it, or -1.</summary>
    public int FreeSlot(int level)
    {
        int open = SlotsOpen(level);
        for (int i = 0; i < open; i++)
        {
            if (Slots[i] is null)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Catches an offer into the place (a click never fails); false when the place is closed or taken, or the offer is not of the day, or the species is already in the team.</summary>
    public bool Catch(int slot, Offer offer, int level, DateTime day)
    {
        if (slot < 0 || slot >= Slots.Length || slot >= SlotsOpen(level) || Slots[slot] is not null || !_offers.Contains(offer) || Slots.Any(m => m?.Path[0] == offer.Id))
        {
            return false;
        }

        var path = new List<int> { offer.Id };
        var rng = new Random(SeedMix(offer.Id * 7 + DayKey(day)));
        while (_dex.Get(path[^1]) is { To.Length: > 0 } entry)
        {
            path.Add(entry.To[rng.Next(entry.To.Length)]);
        }

        Slots[slot] = new TeamMember { Path = path.ToArray(), Shiny = offer.Shiny, CaughtLevel = level, CaughtDay = DayKey(day) };
        Save();
        return true;
    }

    /// <summary>Lets a Pokémon go.</summary>
    public void Release(int slot)
    {
        if (slot >= 0 && slot < Slots.Length && Slots[slot] is not null)
        {
            Slots[slot] = null;
            _offersDay = 0; // the day's draw may now include the species again
            Save();
        }
    }

    /// <summary>The members that reached a new form since the last call, as (place, old form id, new form id); each is told once.</summary>
    public List<(int Slot, int From, int To)> TakeEvolutions(int level)
    {
        var list = new List<(int, int, int)>();
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i] is TeamMember m)
            {
                int stage = StageAt(m, level);
                if (stage > m.ShownStage)
                {
                    list.Add((i, m.Path[m.ShownStage], m.Path[stage]));
                    m.ShownStage = stage;
                }
            }
        }

        if (list.Count > 0)
        {
            Save();
        }

        return list;
    }

    /// <summary>Marks every member as shown at its current form without telling (the meter is just starting).</summary>
    public void Sync(int level)
    {
        bool changed = false;
        foreach (TeamMember? m in Slots)
        {
            if (m is not null && StageAt(m, level) != m.ShownStage)
            {
                m.ShownStage = StageAt(m, level);
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    public static PokemonTeam Load(PokemonDex dex, string file)
    {
        try
        {
            if (File.Exists(file))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                long seed = doc.RootElement.TryGetProperty("seed", out JsonElement s) && s.TryGetInt64(out long v) ? v : 0;
                var team = new PokemonTeam(dex, file, seed);
                if (doc.RootElement.TryGetProperty("slots", out JsonElement slots))
                {
                    int i = 0;
                    foreach (JsonElement e in slots.EnumerateArray())
                    {
                        if (i >= team.Slots.Length)
                        {
                            break;
                        }

                        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("path", out JsonElement path))
                        {
                            int[] ids = path.EnumerateArray().Select(x => x.GetInt32()).Where(id => dex.Get(id) is not null).ToArray();
                            if (ids.Length > 0)
                            {
                                team.Slots[i] = new TeamMember
                                {
                                    Path = ids,
                                    Shiny = e.TryGetProperty("shiny", out JsonElement sh) && sh.GetBoolean(),
                                    CaughtLevel = e.TryGetProperty("level", out JsonElement lv) ? lv.GetInt32() : 1,
                                    CaughtDay = e.TryGetProperty("day", out JsonElement dy) ? dy.GetInt32() : 0,
                                    ShownStage = e.TryGetProperty("shown", out JsonElement sn) ? sn.GetInt32() : 0,
                                };
                            }
                        }

                        i++;
                    }
                }

                return team;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            // a broken file: an empty team (a new seed)
        }

        return new PokemonTeam(dex, file);
    }

    public void Save()
    {
        if (_file is null)
        {
            return;
        }

        try
        {
            var slots = Slots.Select(m => m is null ? null : (object)new { path = m.Path, shiny = m.Shiny, level = m.CaughtLevel, day = m.CaughtDay, shown = m.ShownStage }).ToArray();
            File.WriteAllText(_file, JsonSerializer.Serialize(new { seed = Seed, slots }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the next change writes it again
        }
    }
}
