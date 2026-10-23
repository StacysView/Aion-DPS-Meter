using AionDPS.History;

namespace AionDPS.Combat;

/// <summary>One use of a skill by the local player, as the game acknowledged it (frame 0x0138):
/// accepted, or refused with a code (a key pressed during an animation, a cooldown). Times are
/// capture time, the same clock as <see cref="DamageEvent.Timestamp"/>.</summary>
public readonly record struct SkillUse(DateTime At, int SkillId, bool Accepted, int RejectCode = 0);

/// <summary>An effect (buff, potion, food, debuff) put on the local player or taken off. Times are capture time.</summary>
public readonly record struct EffectChange(DateTime At, int EffectId, bool On);

/// <summary>The local player's stat table at a moment (frame 0x4a36: stat id to value). Two runs with a
/// different table were not fought with the same gear, so their damage is not comparable.</summary>
public sealed record StatSnapshot(DateTime At, IReadOnlyDictionary<int, int> Values);

/// <summary>
/// One timed run at a training dummy: the local player's hits, skill uses and effects inside
/// [<see cref="Start"/>, <see cref="Start"/> + <see cref="Planned"/>], plus everything needed to judge whether
/// two runs can be compared (levels, stats, target). Pure data: the state machine that fills it is
/// <c>DummyTest</c>, the statistics are <c>DummyAnalysis</c>.
/// </summary>
public sealed class DummyRun
{
    public long Id { get; set; }

    /// <summary>The run's number in the session list (1, 2, 3 ...).</summary>
    public int Number { get; init; }

    /// <summary>What the player calls this setup ("Smash 20 / Cleave 18"); runs with the same label and level signature pool into one configuration.</summary>
    public string Label { get; set; } = "";

    public string Note { get; set; } = "";

    /// <summary>The first own hit, skill use or effect after Arm, in capture time: the clock began here.</summary>
    public DateTime Start { get; set; }

    /// <summary>The time the player asked for. Only runs with the same value are compared.</summary>
    public TimeSpan Planned { get; init; }

    /// <summary>How long the run really lasted: <see cref="Planned"/>, shorter when stopped early.</summary>
    public TimeSpan Length { get; set; }

    /// <summary>False when stopped early: kept in the list, never used in a comparison.</summary>
    public bool Complete => Length >= Planned;

    public DateTime End => Start + Length;

    public string TargetName { get; set; } = "";

    /// <summary>The local player's combat id for this session (the events' source id).</summary>
    public int PlayerId { get; set; }

    /// <summary>The local player's hits (summons attributed to the owner) inside the window, heals left out.</summary>
    public List<DamageEvent> Events { get; set; } = new();

    public List<SkillUse> Uses { get; set; } = new();

    public List<EffectChange> Effects { get; set; } = new();

    public Dictionary<int, string> Names { get; set; } = new();

    /// <summary>The skill levels as the game last listed them before the run; null when unknown.</summary>
    public List<SkillLevel>? Levels { get; set; }

    /// <summary>When the last skill frame behind <see cref="Levels"/> arrived (capture time); null when unknown.</summary>
    public DateTime? LevelsReadAt { get; set; }

    /// <summary>True when the player confirmed the levels (the "skills read" step of the setup was green at Start).</summary>
    public bool LevelsConfirmed { get; set; }

    public StatSnapshot? Stats { get; set; }

    public DateTime SavedAt { get; set; }
}
