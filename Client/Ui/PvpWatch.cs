using AionDPS.Aion2;

namespace AionDPS.Ui;

public enum PvpAlertKind
{
    EnemyNear,
    TargetedByEnemy,
}

public sealed record PvpAlert(PvpAlertKind Kind, string Name, int? Power);

/// <summary>
/// The PvP side of the meter (Stacy, 2026-11-14): in the Abyss (zones 20, 22, 23) everything PvP is on, out of it everything is off. While on, it tells when
/// an enemy comes into view and when one marks the local player as target. An enemy is a player of another server (the Abyss matches servers against each
/// other; the camp is no use there: the opponents are of the same faction). Pure: the directory and the time are handed in, so a test can walk through it.
/// A player whose server is not known (the game names it only for some) is not guessed: no alert for him until it is. Party members never count.
/// The "enemy near" alert is one sound for the first enemy who comes while nobody was there (Stacy, 2026-11-14): while an enemy stays in view the others who
/// join him make no sound; it is armed again once no enemy has been in view for <see cref="EmptyFor"/>.
/// </summary>
public sealed class PvpWatch
{
    /// <summary>The Abyss zones: lower, middle and upper Chaos.</summary>
    public static bool IsAbyss(int zoneId) => zoneId is 20 or 22 or 23;

    /// <summary>The same player does not set off the same alert more often than this.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    /// <summary>No enemy in view for this long before the "enemy near" alert may sound again.</summary>
    public static readonly TimeSpan EmptyFor = TimeSpan.FromSeconds(1);

    private DateTime? _emptySince;
    private bool _nearArmed;
    private readonly Dictionary<int, DateTime> _lastTargeted = new();
    private long _targetSeq;
    private bool _active;

    /// <summary>Whether the watch is on (the player is in the Abyss).</summary>
    public bool Active => _active;

    /// <summary>One look. Returns the alerts to play now (none while out of the Abyss). <paramref name="near"/> and <paramref name="targeted"/> are the two options.</summary>
    public IReadOnlyList<PvpAlert> Poll(Aion2EntityDirectory entities, int zoneId, DateTime now, bool near, bool targeted)
    {
        bool inAbyss = IsAbyss(zoneId);
        if (!inAbyss)
        {
            _active = false;
            return [];
        }

        entities.TargetsSince(_targetSeq, out long total);
        if (!_active)
        {
            // Just came in: what is already in view is no news.
            _active = true;
            _lastTargeted.Clear();
            _emptySince = null;
            _nearArmed = false;
            _targetSeq = total;
            return [];
        }

        var alerts = new List<PvpAlert>();
        int me = entities.LocalPlayerId;
        var party = entities.PartyNames;
        // The enemies in view now (the first-sight frame marks them, the "gone" frame takes them away); the state is kept up even while the option is off.
        (int Id, string Name)? first = null;
        foreach (int id in entities.HostilePlayersInView())
        {
            if (id != me && entities.NameFor(id) is { } name && !party.Contains(name))
            {
                first = (id, name);
                break;
            }
        }

        if (first is null)
        {
            _emptySince ??= now;
            _nearArmed |= now - _emptySince.Value >= EmptyFor;
        }
        else
        {
            _emptySince = null;
            if (_nearArmed)
            {
                _nearArmed = false;
                if (near)
                {
                    alerts.Add(new PvpAlert(PvpAlertKind.EnemyNear, first.Value.Name, entities.SeenProfileOf(first.Value.Id)?.CombatPower));
                }
            }
        }

        var since = entities.TargetsSince(_targetSeq, out total);
        _targetSeq = total;
        if (targeted && me >= 0)
        {
            foreach ((int actor, int target, DateTime at) in since)
            {
                if (target != me || actor == me)
                {
                    continue;
                }

                string? name = entities.NameFor(actor);
                if (name is null || party.Contains(name) || entities.SeenProfileOf(actor) is null || !entities.IsHostilePlayer(actor))
                {
                    continue; // a monster, a party member or a friend: not a threat
                }

                if (_lastTargeted.TryGetValue(actor, out DateTime last) && now - last < Cooldown)
                {
                    continue;
                }

                _lastTargeted[actor] = now;
                alerts.Add(new PvpAlert(PvpAlertKind.TargetedByEnemy, name, entities.SeenProfileOf(actor)?.CombatPower));
            }
        }

        return alerts;
    }
}
