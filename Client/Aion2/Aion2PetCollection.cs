using System.IO;
using System.Text.Json;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>One pet as the game counts it: level 0 = only souls so far, 1-3 = collected and raised,
/// <see cref="Souls"/> = the souls given since the last level.</summary>
public sealed record Aion2PetState(int PetId, int Level, int Souls)
{
    /// <summary>Souls needed for the next level (5, 25, 75), 0 at the top level.</summary>
    public int Needed => Level switch { 0 => 5, 1 => 25, 2 => 75, _ => 0 };

    public bool IsTop => Level >= 3;
}

/// <summary>
/// The local player's pets, kept from the game's own frames: the login list (levels and souls of every
/// pet), the soul gains and the level-ups. Frames are decoded on the capture thread and the window reads
/// on the UI thread, so the state is guarded by a lock, readers get copies and events are raised outside
/// the lock.
/// </summary>
public sealed class Aion2PetCollection
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Aion2PetState> _pets = new();
    private DateTime? _listedAt;
    private string? _character;

    /// <summary>A snapshot of every known pet.</summary>
    public IReadOnlyDictionary<int, Aion2PetState> Pets
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<int, Aion2PetState>(_pets);
            }
        }
    }

    /// <summary>When the login list was last received; null until then.</summary>
    public DateTime? ListedAt
    {
        get { lock (_gate) { return _listedAt; } }
    }

    /// <summary>The character the data belongs to (null while unknown); set by the owner.</summary>
    public string? Character
    {
        get { lock (_gate) { return _character; } }
        set { lock (_gate) { _character = value; } }
    }

    /// <summary>Raised after a soul gain or a level frame: the pet's new state and whether it levelled up
    /// (or was collected).</summary>
    public event Action<Aion2PetState, bool>? Changed;

    /// <summary>Raised after the whole list was replaced (login list or a restore from disk).</summary>
    public event Action? ListReplaced;

    /// <summary>1 while any catalog pet is below level 1, then 2 while any is below 2, then 3.</summary>
    public int Phase
    {
        get
        {
            lock (_gate)
            {
                for (int phase = 1; phase < 3; phase++)
                {
                    if (BelowLocked(phase).Any())
                    {
                        return phase;
                    }
                }

                return 3;
            }
        }
    }

    /// <summary>Pets below <paramref name="phase"/>, the catalog pets missing from the list included at
    /// level 0 / 0 souls; by pet id.</summary>
    public IReadOnlyList<Aion2PetState> Needed(int phase)
    {
        lock (_gate)
        {
            return BelowLocked(phase).ToList();
        }
    }

    private IEnumerable<Aion2PetState> BelowLocked(int phase)
    {
        var result = new Dictionary<int, Aion2PetState>();
        foreach (int id in Aion2PetCatalog.PetIds)
        {
            if (!_pets.ContainsKey(id))
            {
                result[id] = new Aion2PetState(id, 0, 0);
            }
        }

        foreach (Aion2PetState pet in _pets.Values)
        {
            result[pet.PetId] = pet;
        }

        return result.Values.Where(p => p.Level < phase).OrderBy(p => p.PetId);
    }

    /// <summary>Replaces everything with the game's login list.</summary>
    public void ApplyList(IEnumerable<(int Id, int Level)> levels, IEnumerable<(int Id, int Souls)> souls, DateTime at)
    {
        lock (_gate)
        {
            _pets.Clear();
            foreach ((int id, int level) in levels)
            {
                _pets[id] = new Aion2PetState(id, level, 0);
            }

            foreach ((int id, int count) in souls)
            {
                _pets[id] = _pets.TryGetValue(id, out Aion2PetState? known) ? known with { Souls = count } : new Aion2PetState(id, 0, count);
            }

            _listedAt = at;
        }

        ListReplaced?.Invoke();
    }

    /// <summary>Adds souls to a pet (unknown pets start at level 0). A level-0 pet reaching 5 souls is
    /// collected: level 1 with the overflow. Further levels come from the game's level frame.</summary>
    public void ApplySouls(int petId, int gained)
    {
        if (gained is < 1 or > 999)
        {
            return;
        }

        Aion2PetState state;
        bool leveledUp = false;
        lock (_gate)
        {
            Aion2PetState current = _pets.GetValueOrDefault(petId) ?? new Aion2PetState(petId, 0, 0);
            state = current with { Souls = current.Souls + gained };
            if (state.Level == 0 && state.Souls >= state.Needed)
            {
                state = state with { Level = 1, Souls = state.Souls - state.Needed };
                leveledUp = true;
            }

            _pets[petId] = state;
        }

        Changed?.Invoke(state, leveledUp);
    }

    /// <summary>Sets a pet's level from the game's level frame; the souls start again at 0. A level frame
    /// is always reported as a level-up (unless it lowers the level).</summary>
    public void ApplyLevel(int petId, int level)
    {
        if (level is < 0 or > 3)
        {
            return;
        }

        Aion2PetState state;
        bool leveledUp;
        lock (_gate)
        {
            int old = _pets.TryGetValue(petId, out Aion2PetState? current) ? current.Level : 0;
            state = new Aion2PetState(petId, level, 0);
            _pets[petId] = state;
            leveledUp = level >= old;
        }

        Changed?.Invoke(state, leveledUp);
    }

    /// <summary>Takes over another collection's pets (a loaded file); the character stays as it is.</summary>
    public void Restore(Aion2PetCollection other)
    {
        Dictionary<int, Aion2PetState> pets = new(other.Pets);
        DateTime? listedAt = other.ListedAt;
        lock (_gate)
        {
            _pets.Clear();
            foreach (var pair in pets)
            {
                _pets[pair.Key] = pair.Value;
            }

            _listedAt = listedAt;
        }

        ListReplaced?.Invoke();
    }

    private sealed class SavedPet
    {
        public int PetId { get; set; }
        public int Level { get; set; }
        public int Souls { get; set; }
    }

    private sealed class SavedCollection
    {
        public string? Character { get; set; }
        public DateTime? ListedAt { get; set; }
        public List<SavedPet> Pets { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>The save file of a character inside <paramref name="folder"/>: pets-&lt;name&gt;.json.</summary>
    public static string PathFor(string folder, string character)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string name = new(character.Trim().Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        return Path.Combine(folder, "pets-" + name + ".json");
    }

    public void Save(string path)
    {
        SavedCollection saved;
        lock (_gate)
        {
            saved = new SavedCollection
            {
                Character = _character,
                ListedAt = _listedAt,
                Pets = _pets.Values.OrderBy(p => p.PetId).Select(p => new SavedPet { PetId = p.PetId, Level = p.Level, Souls = p.Souls }).ToList(),
            };
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(saved, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not saved this time; the next change tries again.
        }
    }

    /// <summary>The collection saved at <paramref name="path"/>; empty when the file is missing or broken.</summary>
    public static Aion2PetCollection Load(string path)
    {
        var collection = new Aion2PetCollection();
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<SavedCollection>(File.ReadAllText(path)) is { } saved)
            {
                collection._character = saved.Character;
                collection._listedAt = saved.ListedAt;
                foreach (SavedPet pet in saved.Pets)
                {
                    collection._pets[pet.PetId] = new Aion2PetState(pet.PetId, pet.Level, pet.Souls);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            collection._pets.Clear();
            collection._listedAt = null;
        }

        return collection;
    }
}
