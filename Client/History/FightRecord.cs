using AionDPS.Combat;

namespace AionDPS.History;

/// <summary>One row of the history list.</summary>
public sealed record FightSummary(
    long Id,
    string Game,
    string? ServerName,
    DateTime StartedAt,
    DateTime EndedAt,
    string TargetName,
    string Kind,
    long TotalDamage,
    int ParticipantCount,
    string? SelfName)
{
    public TimeSpan Duration => EndedAt - StartedAt;

    /// <summary>A fight against a training dummy (see <see cref="DummyTargets"/>) - the ones the
    /// dummy lab compares.</summary>
    public bool IsDummy => DummyTargets.IsDummy(TargetName);

    public string DurationDisplay => $"{(int)Duration.TotalMinutes}:{Duration.Seconds:D2}";
}

public sealed record FightParticipant(
    string Name,
    string ClassName,
    string Faction,
    bool IsSelf,
    bool IsEnemy,
    long Damage,
    double? Dps,
    long Healing,
    long DamageTaken)
{
    public string DpsDisplay => Dps is double d ? d.ToString("F0") : "n/a";
}

/// <summary>One of the local player's skills as the game listed it at login: total level, and the
/// trained part of it (total = base + bonuses). See Aion2EntityDirectory.LocalSkills.</summary>
public sealed record SkillLevel(int SkillId, int Level, int BaseLevel);

/// <summary>A stored fight with everything needed to show it again: the raw events plus the
/// id→name table they were recorded under (ids are per-session, so a replay has to remap them).
/// <paramref name="SkillLevels"/> is the local player's skill levels when the fight was saved - null
/// for fights saved before it existed or while the game's skill list was unknown.</summary>
public sealed record FightDetail(
    FightSummary Summary,
    IReadOnlyList<FightParticipant> Participants,
    IReadOnlyList<DamageEvent> Events,
    IReadOnlyDictionary<int, string> Names,
    IReadOnlyList<SkillLevel>? SkillLevels = null);
