using AionDPS.Combat.Sources;

namespace AionDPS.Aion2;

/// <summary>One equipped item as the character record lists it: its position and its id.</summary>
public sealed record Aion2EquippedItem(int SlotIndex, int ItemId, int Enchant = 0);

/// <summary>One learned skill: total level (what the game shows) and the trained base level; the
/// difference is a bonus from gear or other sources.</summary>
public sealed record Aion2SkillEntry(int SkillId, int Level, int BaseLevel);

/// <summary>The last skill frame the game sent: when it arrived (capture time), its opcode (the login list or a single entry) and how many
/// skill levels were known after it. For the "Skills read" line of the dummy test setup.</summary>
public sealed record Aion2SkillFrame(DateTime At, int Opcode, int Count);

/// <summary>What the "player appeared" frame says about another player: class and faction (decoded
/// from its class code) and the visible equipment (no enchant levels in that list).</summary>
public sealed record Aion2SeenProfile(int? ClassId, int? Faction, IReadOnlyList<Aion2EquippedItem> Gear);

/// <summary>The activated node ids of one Daevanion board (the start node included).</summary>
public sealed record Aion2DaevanionBoard(int BoardId, IReadOnlyList<int> NodeIds);

/// <summary>The local player's character record (opcode 0x3336), as of when the server last sent it
/// - at login and on every zone change.</summary>
public sealed record Aion2CharacterInfo(int CombatId, string Name, int ClassCode, int Level, IReadOnlyList<Aion2EquippedItem> Equipment, DateTime ReceivedAt, bool Restored = false, int ServerId = 0);

/// <summary>Aion 2 frames carry the game's own object ids, so this maps those to names as nickname
/// frames reveal them. The local player is whichever id the session frame names. Names that never
/// came with a game id (hand-entered characters) get synthetic negative ids so they can never
/// collide with a real object id.</summary>
public sealed class Aion2EntityDirectory : IEntityDirectory
{
    private readonly Dictionary<int, string> _names = new();
    private readonly Dictionary<string, int> _ids = new();
    // Per object: how often each class's skills were seen with it as the actor. A received buff
    // (a Chanter's Mantra on a Gladiator) arrives with the recipient as actor, so the LAST class
    // seen is wrong - the most frequent one is right.
    private readonly Dictionary<int, Dictionary<string, int>> _classVotes = new();
    private readonly object _gate = new();

    // Name -> how many roster frames listed it. The party's own members are listed in every update;
    // a stray look-alike (a member who left, a byte run that happens to fit) is listed once.
    private readonly Dictionary<string, int> _roster = new(StringComparer.Ordinal);
    private readonly HashSet<string> _notPlayers = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _guilds = new();
    private int _explicitLocalId = -1;
    private Aion2CharacterInfo? _character;
    private string? _configuredLocalName;

    /// <summary>
    /// The local player: a session frame's id when one is decoded, else the object whose name is the
    /// configured character name (<see cref="SetConfiguredLocalName"/>), else the one inferred from
    /// the stream (see <see cref="InferLocalPlayer"/>). -1 while none is known.
    /// </summary>
    public int LocalPlayerId
    {
        get
        {
            if (_explicitLocalId >= 0)
            {
                return _explicitLocalId;
            }

            lock (_gate)
            {
                if (_configuredLocalName is not null && _ids.TryGetValue(_configuredLocalName, out int byName))
                {
                    return byName;
                }
            }

            return InferLocalPlayer() ?? -1;
        }

        internal set => _explicitLocalId = value;
    }

    /// <summary>The user's own Aion 2 character name from Settings (empty = none).</summary>
    public void SetConfiguredLocalName(string? name)
    {
        lock (_gate)
        {
            _configuredLocalName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
    }

    /// <summary>The local player's name as its own character record states it - what the meter then
    /// remembers in Settings so the next session knows it from the start. It used to be the party
    /// roster's leftover name (the one no visible player carries), which, once the roster was read on
    /// every server, could be a team mate not named yet: that name was then saved as one's own and the
    /// own row showed under a team mate's name.</summary>
    public string? LearnedLocalName
    {
        get
        {
            lock (_gate)
            {
                return _character is { Restored: false } own && own.Name.Length > 0 ? own.Name : null;
            }
        }
    }

    /// <summary>The local player's own record. It is only ever sent for yourself (everyone else gets
    /// the "appeared" frame), so it also settles who the local player is.
    /// <para>A zone change gives the local player a new combat id. The summons still out (a spirit
    /// following them across) go to the new id, and the old id's events are to be merged into it
    /// (see <see cref="DrainMovedIds"/>): a Water Spirit's two blows under the old id made a second
    /// "Boulenbouche" row (2026-10-11 16:46). The old id loses the name and the class - the server
    /// hands it to something else later (a monster once showed under it, 2026-10-08) - and nothing
    /// is kept to send its future events to the player. The class goes to the new id: a player is
    /// one the class is known of (<see cref="IsKnownPlayer"/>), and the new id only got it with the
    /// first spell cast there, 30 s to 1 min later (2026-10-10 capture) - meanwhile the player's row
    /// showed an empty detail and the fight went to the history without them (2026-10-12).</para></summary>
    public void SetLocalCharacter(Aion2CharacterInfo info)
    {
        lock (_gate)
        {
            int previous = _explicitLocalId;
            if (previous >= 0 && previous != info.CombatId && _names.GetValueOrDefault(previous) == info.Name)
            {
                foreach (int summon in _summonOwners.Where(kv => kv.Value == previous).Select(kv => kv.Key).ToList())
                {
                    _summonOwners[summon] = info.CombatId;
                }

                _names.Remove(previous);
                // Over any class the new id had: ids are reused, it may have been someone else's.
                if (_classVotes.Remove(previous, out var votes))
                {
                    _classVotes[info.CombatId] = votes;
                }

                _movedIds.Add((previous, info.CombatId));
            }

            _character = info;
            _explicitLocalId = info.CombatId;
            _names[info.CombatId] = info.Name;
            _ids[info.Name] = info.CombatId;
        }

        CharacterChanged?.Invoke(info);
    }

    // The local player's id changes since the last DrainMovedIds (old id, new id).
    private readonly List<(int From, int To)> _movedIds = new();

    /// <summary>The local player's id changes (zone changes) since the last call: the events of the
    /// old id are the player's, to be merged into the new one.</summary>
    public IReadOnlyList<(int From, int To)> DrainMovedIds()
    {
        lock (_gate)
        {
            var drained = _movedIds.ToList();
            _movedIds.Clear();
            return drained;
        }
    }

    public Aion2CharacterInfo? LocalCharacter
    {
        get
        {
            lock (_gate)
            {
                return _character;
            }
        }
    }

    public event Action<Aion2CharacterInfo>? CharacterChanged;

    /// <summary>Loads what an earlier session saved (see <see cref="Aion2CharacterStore"/>). The combat
    /// id of a saved record is meaningless in this session, so it is not made the local player; the
    /// record just fills the Character view and the profile upload until the game sends the real one.</summary>
    public void RestoreFrom(Aion2SavedCharacter saved)
    {
        lock (_gate)
        {
            if (_character is not null)
            {
                return; // fresh data already arrived
            }

            var equipment = saved.Equipment.Select(i => new Aion2EquippedItem(i.Slot, i.ItemId, i.Enchant)).ToList();
            _character = new Aion2CharacterInfo(-1, saved.Name, saved.ClassCode, saved.Level, equipment, saved.SavedAt, Restored: true, ServerId: saved.ServerId);
            _fullEquipment = equipment;
            _skills = saved.Skills.Select(s => new Aion2SkillEntry(s.Id, s.Level, s.BaseLevel)).ToList();
            _daevanion = saved.Daevanion.Select(b => new Aion2DaevanionBoard(b.Board, b.Nodes)).ToList();
        }
    }

    /// <summary>The local player's data as it should be kept on disk, or null before any is known.</summary>
    public Aion2SavedCharacter? ToSaved()
    {
        lock (_gate)
        {
            if (_character is not { Restored: false } c)
            {
                return null;
            }

            return new Aion2SavedCharacter
            {
                Name = c.Name,
                ClassCode = c.ClassCode,
                Level = c.Level,
                ServerId = c.ServerId,
                SavedAt = DateTime.Now,
                Equipment = (_fullEquipment ?? c.Equipment).Select(i => new Aion2SavedCharacter.SavedItem(i.SlotIndex, i.ItemId, i.Enchant)).ToList(),
                Skills = (_skills ?? Array.Empty<Aion2SkillEntry>()).Select(s => new Aion2SavedCharacter.SavedSkill(s.SkillId, s.Level, s.BaseLevel)).ToList(),
                Daevanion = (_daevanion ?? Array.Empty<Aion2DaevanionBoard>()).Select(b => new Aion2SavedCharacter.SavedBoard(b.BoardId, b.NodeIds.ToList())).ToList(),
            };
        }
    }

    private IReadOnlyList<Aion2EquippedItem>? _fullEquipment;
    private IReadOnlyList<Aion2SkillEntry>? _skills;

    /// <summary>Every slot with its enchant level (the login equipment record); falls back to the
    /// 11 visible slots of the character record until that arrives.</summary>
    public IReadOnlyList<Aion2EquippedItem> LocalEquipment
    {
        get
        {
            lock (_gate)
            {
                return _fullEquipment ?? _character?.Equipment ?? Array.Empty<Aion2EquippedItem>();
            }
        }
    }

    public IReadOnlyList<Aion2SkillEntry> LocalSkills
    {
        get
        {
            lock (_gate)
            {
                return _skills ?? Array.Empty<Aion2SkillEntry>();
            }
        }
    }

    public void SetLocalEquipment(IReadOnlyList<Aion2EquippedItem> equipment)
    {
        lock (_gate)
        {
            _fullEquipment = equipment;
        }

        NotifyCharacterChanged();
    }

    private IReadOnlyList<Aion2DaevanionBoard>? _daevanion;

    public IReadOnlyList<Aion2DaevanionBoard> LocalDaevanion
    {
        get
        {
            lock (_gate)
            {
                return _daevanion ?? Array.Empty<Aion2DaevanionBoard>();
            }
        }
    }

    public void SetLocalDaevanion(IReadOnlyList<Aion2DaevanionBoard> boards)
    {
        lock (_gate)
        {
            _daevanion = boards;
        }

        NotifyCharacterChanged();
    }

    public void SetLocalSkills(IReadOnlyList<Aion2SkillEntry> skills)
    {
        lock (_gate)
        {
            _skills = skills;
        }

        NotifyCharacterChanged();
    }

    // The skill entries the game sends one by one (frames 0x1338, 0x0F38, 0x1038; layouts by SkeeveTV, see Aion2FrameDecoder.DecodeSkillEntry):
    // a base skill (id ending in 0000) belongs to LocalSkills like the login list's; a chosen variant (Rending Blow 11010340 beside 11010000) is
    // kept apart, so what the character window shows and the profile sends does not change. Both lists are replaced, never edited, so a reader
    // holding one keeps a consistent list.
    private IReadOnlyList<Aion2SkillEntry>? _skillVariants;
    private Aion2SkillFrame? _lastSkillFrame;

    /// <summary>The variant skills the game listed one by one (id not ending in 0000), with their own levels. Empty until such a frame arrives.</summary>
    public IReadOnlyList<Aion2SkillEntry> LocalSkillVariants
    {
        get
        {
            lock (_gate)
            {
                return _skillVariants ?? Array.Empty<Aion2SkillEntry>();
            }
        }
    }

    /// <summary>Every skill level known now: <see cref="LocalSkills"/> and <see cref="LocalSkillVariants"/> together, for what compares levels.</summary>
    public IReadOnlyList<Aion2SkillEntry> LocalSkillLevels
    {
        get
        {
            lock (_gate)
            {
                return (_skills ?? Array.Empty<Aion2SkillEntry>()).Concat(_skillVariants ?? Array.Empty<Aion2SkillEntry>()).ToList();
            }
        }
    }

    /// <summary>The last skill frame the game sent (the login list or a single entry), as of when it arrived; null before one. The restored,
    /// saved list does not count: it says nothing about what the game has now.</summary>
    public Aion2SkillFrame? LastSkillFrame
    {
        get
        {
            lock (_gate)
            {
                return _lastSkillFrame;
            }
        }
    }

    /// <summary>Notes that a skill frame with this opcode arrived at this capture time (whether or not it changed a level).</summary>
    public void NoteSkillFrame(DateTime at, int opcode)
    {
        lock (_gate)
        {
            _lastSkillFrame = new Aion2SkillFrame(at, opcode, (_skills?.Count ?? 0) + (_skillVariants?.Count ?? 0));
        }
    }

    /// <summary>One skill's new level from a single-entry frame: replaces the entry of that skill, or adds it. The rest of the list stays.</summary>
    public void UpdateLocalSkill(Aion2SkillEntry entry)
    {
        bool listChanged = false;
        lock (_gate)
        {
            bool inBase = _skills is not null && _skills.Any(s => s.SkillId == entry.SkillId);
            if (inBase || entry.SkillId % 10000 == 0)
            {
                listChanged = Replace(ref _skills, entry);
            }
            else
            {
                Replace(ref _skillVariants, entry);
            }
        }

        if (listChanged)
        {
            NotifyCharacterChanged();
        }
    }

    /// <summary>A skill entry the game took away (a variant that was swapped out). A base skill is never removed this way.</summary>
    public void RemoveLocalSkill(int skillId)
    {
        bool listChanged = false;
        lock (_gate)
        {
            if (_skillVariants is not null && _skillVariants.Any(s => s.SkillId == skillId))
            {
                _skillVariants = _skillVariants.Where(s => s.SkillId != skillId).ToList();
            }

            if (skillId % 10000 != 0 && _skills is not null && _skills.Any(s => s.SkillId == skillId))
            {
                _skills = _skills.Where(s => s.SkillId != skillId).ToList();
                listChanged = true;
            }
        }

        if (listChanged)
        {
            NotifyCharacterChanged();
        }
    }

    // True when the list changed.
    private static bool Replace(ref IReadOnlyList<Aion2SkillEntry>? list, Aion2SkillEntry entry)
    {
        var next = list is null ? new List<Aion2SkillEntry>() : new List<Aion2SkillEntry>(list);
        int at = next.FindIndex(s => s.SkillId == entry.SkillId);
        if (at >= 0)
        {
            if (next[at] == entry)
            {
                return false;
            }

            next[at] = entry;
        }
        else
        {
            next.Add(entry);
        }

        list = next;
        return true;
    }

    private Combat.StatSnapshot? _stats;

    /// <summary>The local player's stat table (frame 0x4A36) as last updated, or null before one arrived. Immutable: a new snapshot replaces it.</summary>
    public Combat.StatSnapshot? LocalStats
    {
        get
        {
            lock (_gate)
            {
                return _stats;
            }
        }
    }

    /// <summary>Merges stat changes into the local player's table (the first, full table and every later update, which carries only what changed).
    /// Returns false when nothing differed - a heartbeat or a repeated value.</summary>
    public bool NoteLocalStats(DateTime at, IReadOnlyList<(int Id, int Value)> changes)
    {
        lock (_gate)
        {
            Dictionary<int, int>? next = null;
            foreach ((int id, int value) in changes)
            {
                if (_stats is not null && _stats.Values.TryGetValue(id, out int old) && old == value)
                {
                    continue;
                }

                next ??= _stats is null ? new Dictionary<int, int>() : new Dictionary<int, int>(_stats.Values);
                next[id] = value;
            }

            if (next is null)
            {
                return false;
            }

            _stats = new Combat.StatSnapshot(at, next);
            return true;
        }
    }

    /// <summary>What the local player did - skill uses, effects, opcodes seen (see <see cref="Aion2OwnActivity"/>).</summary>
    public Aion2OwnActivity Own { get; } = new();

    private void NotifyCharacterChanged()
    {
        Aion2CharacterInfo? current = LocalCharacter;
        if (current is not null)
        {
            CharacterChanged?.Invoke(current);
        }
    }

    private readonly Dictionary<int, Aion2SeenProfile> _seen = new();

    // Entity id -> NPC id, kept only for monsters that are bosses (see Aion2BossCatalog).
    private readonly Dictionary<int, int> _bossNpcs = new();

    // Entity id -> NPC id of every monster announced (the boss cast bar names elite monsters with it, see Aion2Npcs).
    private readonly Dictionary<int, int> _npcs = new();

    /// <summary>Remembers the NPC id of the monster with this entity id, and that it is a boss when it is one. Called
    /// when the game announces a monster.</summary>
    public void RegisterNpc(int entityId, int npcId)
    {
        lock (_gate)
        {
            _npcs[entityId] = npcId;
            // a boss stays known under its id even when the id is handed out again (the fight history names it); NpcIdOf tells the new one
            if (Protocol.Aion2BossCatalog.Find(npcId) is not null)
            {
                _bossNpcs[entityId] = npcId;
            }
        }
    }

    /// <summary>The NPC id of a monster the game announced, or null.</summary>
    public int? NpcIdOf(int entityId)
    {
        lock (_gate)
        {
            return _npcs.TryGetValue(entityId, out int npcId) ? npcId : null;
        }
    }

    /// <summary>The skills being wound up now (the windup frames), for the boss cast bar.</summary>
    public Aion2BossCasts Casts { get; } = new();

    /// <summary>Every entity's hit points as the server reports them, and when a monster was reset
    /// to full health (a wipe and retry under the same entity id).</summary>
    public Aion2HitPoints HitPoints { get; } = new();

    // Every entity announced by the monster-appears frame: monsters and summons, never players.
    private readonly HashSet<int> _spawned = new();

    /// <summary>Notes that the server announced this entity as a monster (or a summon).</summary>
    public void NoteSpawned(int entityId)
    {
        lock (_gate)
        {
            _spawned.Add(entityId);
        }
    }

    /// <summary>True for an entity the server announced with the monster-appears frame (monsters
    /// and summons, never players).</summary>
    public bool IsSpawned(int entityId)
    {
        lock (_gate)
        {
            return _spawned.Contains(entityId);
        }
    }

    /// <summary>The ids of the party members (see <see cref="PartyNames"/>) who play this class.</summary>
    public IReadOnlyList<int> PartyMemberIdsOfClass(string className)
    {
        var party = PartyNames;
        lock (_gate)
        {
            return party.Where(name => _ids.ContainsKey(name)).Select(name => _ids[name]).Distinct()
                .Where(id => _classVotes.TryGetValue(id, out var votes) && votes.MaxBy(v => v.Value).Key == className)
                .ToList();
        }
    }

    /// <summary>True for an entity the server announced as a monster that is nobody's summon.</summary>
    public bool IsKnownMonster(int entityId)
    {
        lock (_gate)
        {
            return _spawned.Contains(entityId) && !_summonOwners.ContainsKey(entityId) && !_summonOwnerNames.ContainsKey(entityId);
        }
    }

    // Summoned entity id -> the player who summoned it (see Aion2FrameDecoder.DecodeNpcSpawn).
    private readonly Dictionary<int, int> _summonOwners = new();

    /// <summary>Records who summoned an entity, or (null) that it is nobody's summon - entity ids
    /// are reused, so a later spawn under the same id clears an earlier owner.</summary>
    public void SetSummonOwner(int entityId, int? ownerId)
    {
        lock (_gate)
        {
            if (ownerId is int owner)
            {
                if ((!_summonOwners.TryGetValue(entityId, out int known) || known != owner) && _resolvedOwners.Count < 4096)
                {
                    _resolvedOwners.Add((entityId, owner));
                }

                _summonOwners[entityId] = owner;
            }
            else
            {
                _summonOwners.Remove(entityId);
            }
        }
    }

    // Owners found since the last DrainResolvedOwners: hits the summon dealt before (credited to its
    // own id while nobody knew whose it was) can be handed to its owner.
    private readonly List<(int Summon, int Owner)> _resolvedOwners = new();

    /// <summary>The summon owners found since the last call - for re-crediting the hits a summon
    /// dealt before its owner was known (a recording started mid-fight, its first seconds).</summary>
    public IReadOnlyList<(int Summon, int Owner)> DrainResolvedOwners()
    {
        lock (_gate)
        {
            var drained = _resolvedOwners.ToList();
            _resolvedOwners.Clear();
            return drained;
        }
    }

    /// <summary>The player who summoned this entity, or null when it is not a known summon.</summary>
    // Summoned entity id -> its owner's name, for summons announced by name (see
    // Aion2FrameDecoder.DecodeNpcSpawn); resolved through the name -> id map when asked.
    private readonly Dictionary<int, string> _summonOwnerNames = new();

    /// <summary>Records the name a spawned entity carries - a summon's owner (a Cleric's Divine
    /// Aura carries "Psefon"); null clears it, the id being reused by something unnamed.</summary>
    public void SetSummonOwnerName(int entityId, string? ownerName)
    {
        lock (_gate)
        {
            if (ownerName is null)
            {
                _summonOwnerNames.Remove(entityId);
            }
            else
            {
                _summonOwnerNames[entityId] = ownerName;
            }
        }
    }

    public int? SummonOwnerOf(int entityId)
    {
        lock (_gate)
        {
            if (_summonOwners.TryGetValue(entityId, out int owner))
            {
                return owner;
            }

            // Owner known by name: the player of that name, when it is a player and not the entity itself.
            return _summonOwnerNames.TryGetValue(entityId, out string? name) && _ids.TryGetValue(name, out int byName) && byName != entityId
                ? byName
                : null;
        }
    }

    /// <summary>Every monster recognised as a boss so far: entity id and NPC id.</summary>
    public IReadOnlyList<(int EntityId, int NpcId)> KnownBosses()
    {
        lock (_gate)
        {
            return _bossNpcs.Select(kv => (kv.Key, kv.Value)).ToList();
        }
    }

    /// <summary>The boss NPC id of an entity, or null when it is no known boss.</summary>
    public int? BossNpcIdOf(int entityId)
    {
        lock (_gate)
        {
            return _bossNpcs.TryGetValue(entityId, out int npcId) ? npcId : null;
        }
    }

    public void SetSeenProfile(int id, Aion2SeenProfile profile)
    {
        lock (_gate)
        {
            _seen[id] = profile;
        }
    }

    /// <summary>"Elyos" as the network states it: the low bits of the class code are 2 for Elyos
    /// (checked against two Elyos characters and the Elyos legion Akatsuki's member list). The other
    /// value seen (1) also occurs inside that Elyos legion, so it is NOT shown as Asmodian - its
    /// meaning is unknown. From the own character's record or a seen appearance; null otherwise.</summary>
    public string? FactionOf(int id)
    {
        int? bit = IsLocalPlayer(id) && LocalCharacter is { } own && own.ClassCode % 4 is 1 or 2
            ? own.ClassCode % 4
            : SeenProfileOf(id)?.Faction;
        return bit == 2 ? "Elyos" : null;
    }

    /// <summary>The ids of every player whose equipment has been seen so far.</summary>
    public IReadOnlyList<int> SeenProfileIds()
    {
        lock (_gate)
        {
            return _seen.Keys.ToList();
        }
    }

    public Aion2SeenProfile? SeenProfileOf(int id)
    {
        lock (_gate)
        {
            return _seen.GetValueOrDefault(id);
        }
    }

    public void SetGuild(int id, string guild)
    {
        lock (_gate)
        {
            _guilds[id] = guild;
        }
    }

    public string? GuildOf(int id)
    {
        lock (_gate)
        {
            return _guilds.GetValueOrDefault(id);
        }
    }

    /// <summary>Adds a name from the party roster frame. The roster lists the local player too, who
    /// is the one member whose own nickname frame never arrives (everyone else "appears" to you).</summary>
    public void NoteRosterName(string name)
    {
        lock (_gate)
        {
            _roster[name] = _roster.GetValueOrDefault(name) + 1;
        }
    }

    // The members the latest roster frame listed, the local player included.
    private HashSet<string> _party = new(StringComparer.Ordinal);

    /// <summary>
    /// Notes a party roster frame: it lists the group as it is now, so it replaces the previous one.
    /// Every capture with a roster agrees (2026-10-02 to 10-05): a frame changes only when somebody
    /// joins or leaves, and a party breaking up ends with a frame naming the local player alone,
    /// after which none comes. Names were kept for 90 s after their last mention before, so whoever
    /// left within 90 s of that last frame stayed in the group for good.
    /// </summary>
    public void NoteParty(IReadOnlyCollection<string> names, DateTime at)
    {
        lock (_gate)
        {
            _party = new HashSet<string>(names, StringComparer.Ordinal);
            MatchPartyMembersByClass();
        }
    }

    // Party member name -> class, from the roster's class code.
    private readonly Dictionary<string, string> _partyClasses = new(StringComparer.Ordinal);

    // Monsters the local player or a named party member hit, and the unnamed players who hit one too.
    private readonly HashSet<int> _partyTargets = new();
    private readonly HashSet<int> _fightingAlongside = new();

    // Unnamed players fighting alongside the party -> the skills (base ids) they were seen using.
    private readonly Dictionary<int, HashSet<int>> _skillsSeen = new();

    /// <summary>A player uses many skills; a spirit summoned before the meter started (owner
    /// unknown) a few: 16 against 3 on a Krao Cave capture replayed from mid-fight.</summary>
    private const int MinSkillsOfAPlayer = 4;

    /// <summary>True when a name is registered for this id - a player, never a summon.</summary>
    public bool HasName(int id)
    {
        lock (_gate)
        {
            return _names.ContainsKey(id);
        }
    }

    /// <summary>The party's players of a class by id: the members a frame named, and the local
    /// player when it plays that class (its own name may be known only from Settings).</summary>
    public IReadOnlyList<int> PartyPlayerIdsOfClass(string className)
    {
        var ids = PartyMemberIdsOfClass(className).ToList();
        if (InferLocalPlayer() is int local && !ids.Contains(local) && ClassOf(local) == className)
        {
            ids.Add(local);
        }

        return ids;
    }

    /// <summary>Notes a party member's class, as the roster gives it.</summary>
    public void NotePartyClass(string name, string className)
    {
        lock (_gate)
        {
            _partyClasses[name] = className;
        }
    }

    /// <summary>Notes a player's hit on a monster, for <see cref="MatchPartyMembersByClass"/>.</summary>
    public void NoteMonsterHit(int playerId, int monsterId, int skillId)
    {
        lock (_gate)
        {
            bool partySide = _names.TryGetValue(playerId, out string? name)
                ? CurrentPartyNames().Contains(name)
                : IsLocalPlayer(playerId) || InferLocalPlayer() == playerId;
            if (partySide)
            {
                if (_partyTargets.Count > 4096)
                {
                    _partyTargets.Clear();
                }

                _partyTargets.Add(monsterId);
            }
            else if (!_names.ContainsKey(playerId) && !_spawned.Contains(playerId) && _partyTargets.Contains(monsterId))
            {
                _fightingAlongside.Add(playerId);
                if (!_skillsSeen.TryGetValue(playerId, out var skills))
                {
                    skills = new HashSet<int>();
                    _skillsSeen[playerId] = skills;
                }

                if (skills.Add(skillId / 10000) && skills.Count == MinSkillsOfAPlayer)
                {
                    MatchPartyMembersByClass();
                }
            }
        }
    }

    /// <summary>
    /// A player's name arrives only when they "appear" (zone entry, teleport): started inside a
    /// dungeon, the meter knows the party from the roster - names and classes, no combat ids - while
    /// the members fight as unnamed ids. When the party has exactly one member of a class still
    /// without an id, and exactly one unnamed player of that class fights the party's monsters, they
    /// are the same character. Summons do not count (an unclaimed Divine Aura or Bittercold Wind
    /// casts its class's skills too). With two such members or two such players nothing is
    /// guessed; the local player is left to its own rules. (Draupnir and Krao Cave captures
    /// replayed from mid-fight, 2026-10-02.)
    /// </summary>
    private void MatchPartyMembersByClass()
    {
        var party = CurrentPartyNames();
        int? local = _explicitLocalId >= 0 ? _explicitLocalId : InferLocalPlayer();
        string? localName = _character?.Name ?? _configuredLocalName;
        var unnamedMembers = party
            .Where(n => !_ids.ContainsKey(n) && n != localName && _partyClasses.ContainsKey(n))
            .GroupBy(n => _partyClasses[n])
            .Where(g => g.Count() == 1)
            .ToList();
        foreach (var member in unnamedMembers)
        {
            // A spirit summoned before the meter started has no known owner and casts its class's
            // skills too, but few different ones (MinSkillsOfAPlayer). Then one candidate, or one
            // clearly ahead (three times the next one's casts), the same rule as InferLocalPlayer.
            var candidates = _fightingAlongside
                .Where(id => !_names.ContainsKey(id) && id != local && !_spawned.Contains(id)
                    && _skillsSeen.TryGetValue(id, out var skills) && skills.Count >= MinSkillsOfAPlayer
                    && _classVotes.TryGetValue(id, out var votes) && votes.MaxBy(v => v.Value).Key == member.Key)
                .Select(id => (Id: id, Casts: _classVotes[id].Values.Sum()))
                .OrderByDescending(c => c.Casts)
                .ToList();
            if (candidates.Count == 1 || (candidates.Count > 1 && candidates[0].Casts >= 3 * candidates[1].Casts))
            {
                Register(candidates[0].Id, member.First());
                _fightingAlongside.Remove(candidates[0].Id);
            }
        }
    }

    /// <summary>Names in the local player's party: those of the latest roster frame. Empty when no
    /// roster has arrived yet.</summary>
    public IReadOnlySet<string> PartyNames
    {
        get
        {
            lock (_gate)
            {
                return CurrentPartyNames();
            }
        }
    }

    private HashSet<string> CurrentPartyNames() => new(_party, StringComparer.Ordinal);

    /// <summary>A name the roster shows that is not a party member - the guild name, which every
    /// member's nickname frame repeats after its own name.</summary>
    public void NoteNonPlayerName(string name)
    {
        lock (_gate)
        {
            _notPlayers.Add(name);
        }
    }

    // Per entity: a decaying count of its detailed-stats frames (see NoteDetailedStats).
    private readonly Dictionary<int, double> _detailedStats = new();

    /// <summary>
    /// The server sends an entity's detailed stats (the 4-byte group of the stats frame) to that
    /// player alone: on four captures (2026-10-02) the local player received 651 to 1,477 of them,
    /// any other entity 0 to 5. Older counts decay, so after a zone change hands the local player a
    /// new id, the new one takes over within a few frames.
    /// </summary>
    public void NoteDetailedStats(int entityId)
    {
        lock (_gate)
        {
            foreach (int id in _detailedStats.Keys.ToList())
            {
                _detailedStats[id] *= 0.95;
            }

            _detailedStats[entityId] = _detailedStats.GetValueOrDefault(entityId) + 1;
        }
    }

    /// <summary>
    /// The local player, worked out from the stream: first the entity receiving the detailed-stats
    /// frames (see <see cref="NoteDetailedStats"/>) - reliable in a crowd; else the object seen
    /// casting class skills that never got a nickname frame. Only claimed when it is unambiguous - one
    /// such object, or one clearly dominant - otherwise null and nobody is called "you".
    /// </summary>
    public int? InferLocalPlayer()
    {
        lock (_gate)
        {
            if (LocalByDetailedStats() is int byStats)
            {
                return byStats;
            }

            var unnamed = _classVotes.Where(kv => !_names.ContainsKey(kv.Key))
                .Select(kv => (Id: kv.Key, Votes: kv.Value.Values.Sum()))
                .OrderByDescending(x => x.Votes)
                .ToList();
            if (unnamed.Count == 0 || (unnamed.Count > 1 && unnamed[0].Votes < 3 * unnamed[1].Votes))
            {
                return null;
            }

            return unnamed[0].Id;
        }
    }

    // The entity receiving the detailed-stats frames, when one clearly does. Called under _gate.
    private int? LocalByDetailedStats()
    {
        var byStats = _detailedStats.OrderByDescending(kv => kv.Value).Take(2).ToList();
        return byStats.Count > 0 && byStats[0].Value >= 5 && (byStats.Count == 1 || byStats[0].Value >= 3 * byStats[1].Value)
            ? byStats[0].Key
            : null;
    }

    /// <summary>
    /// <see cref="IsLocalPlayer"/> without the weakest guess: the local player as a session frame or the character
    /// record states it, as the configured name names it, or as the detailed-stats frames show it - not the "unnamed
    /// caster" guess, which early in a capture can pick another player before their nickname frame arrives (2026-10-16
    /// 22:42:29: another player for the first five seconds). For what moves the pet map, where a wrong player's
    /// place would stay until the next real one.
    /// </summary>
    public bool IsSettledLocalPlayer(int id)
    {
        if (_explicitLocalId >= 0)
        {
            return id == _explicitLocalId;
        }

        lock (_gate)
        {
            if (_configuredLocalName is not null && _ids.TryGetValue(_configuredLocalName, out int byName))
            {
                return id == byName;
            }

            return LocalByDetailedStats() == id;
        }
    }

    /// <summary>Diagnostic line for the replay tool.</summary>
    public string Describe()
    {
        lock (_gate)
        {
            return $"named [{string.Join(", ", _names.Values)}] roster [{string.Join(", ", _roster.Select(kv => kv.Key + "x" + kv.Value))}] notPlayers [{string.Join(", ", _notPlayers)}] leftover [{LocalRosterName()}]";
        }
    }

    // The roster name nobody else claimed: the local player's, when exactly one is left over.
    private string? LocalRosterName()
    {
        var left = _roster.Where(kv => !_ids.ContainsKey(kv.Key) && !_notPlayers.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .ToList();
        if (left.Count == 0 || (left.Count > 1 && left[0].Value < 3 * left[1].Value))
        {
            return null;
        }

        return left[0].Key;
    }

    /// <summary>The registered name; else, for an object seen casting class skills, "Player #id" -
    /// the combat frames carry only ids, and a player's name arrives separately (and sometimes
    /// late), so this keeps players apart until it does. The class is shown by the row's icon and
    /// class column, so it is deliberately not repeated in the name.</summary>
    public string? NameFor(int id)
    {
        string? registered;
        lock (_gate)
        {
            registered = _names.GetValueOrDefault(id);
            if (registered is null && _bossNpcs.TryGetValue(id, out int npcId) && Protocol.Aion2BossCatalog.Find(npcId) is { } boss)
            {
                return boss.Name;
            }

            // The local player is never announced to itself, so its id has no name of its own until
            // the character record (login, zone change) arrives. Until then: the name set in
            // Settings, else the character saved from the last login, else the party roster's
            // leftover name - solo, only the first two exist, and "Player #id" used to stay.
            if (registered is null && InferLocalPlayer() == id)
            {
                // The roster's leftover name is safe here: the local player is not named yet, so its
                // own name is still among the leftovers, and a single leftover is it.
                registered = _configuredLocalName
                    ?? (_character is { Restored: true } saved && saved.Name.Length > 0 ? saved.Name : null)
                    ?? LocalRosterName();
            }
        }

        return registered ?? (ClassOf(id) is not null ? $"Player #{id}" : null);
    }

    /// <summary>The class an object has most often been seen casting, or null.</summary>
    public string? ClassOf(int id)
    {
        lock (_gate)
        {
            return _classVotes.TryGetValue(id, out var votes) ? votes.MaxBy(v => v.Value).Key : null;
        }
    }

    /// <summary>Remembers which class an object plays, learned from the class prefix of its skills.</summary>
    public void NoteClass(int id, string className)
    {
        lock (_gate)
        {
            if (!_classVotes.TryGetValue(id, out var votes))
            {
                votes = new Dictionary<string, int>();
                _classVotes[id] = votes;
            }

            votes[className] = votes.GetValueOrDefault(className) + 1;
        }
    }

    /// <summary>True once the object has been seen using a class skill - i.e. it is a player.</summary>
    public bool IsKnownPlayer(int id)
    {
        lock (_gate)
        {
            return _classVotes.ContainsKey(id);
        }
    }

    public int GetOrAssignId(string name)
    {
        if (_ids.TryGetValue(name, out int id))
        {
            return id;
        }

        id = -(_ids.Count + 2);
        Register(id, name);
        return id;
    }

    public bool IsLocalPlayer(int id) => LocalPlayerId is >= 0 and var local && id == local;

    public void Register(int id, string name)
    {
        lock (_gate)
        {
            _names[id] = name;
            _ids[name] = id;
        }
    }
}
