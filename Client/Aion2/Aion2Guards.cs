namespace AionDPS.Aion2;

/// <summary>
/// The enemy guards the game has announced around the player (NPC spawn 0x4136, gone with 0x4236), for the red marks on
/// the map: the Asmodian militia guards ("Garde", title "Milice") that kill an Elyos at once, e.g. at the Coldwind Shelter
/// camp in Verteron. The game only announces an NPC within some tens of metres, so a guard shows once the player comes
/// near. Seen on the 2026-10-17 17:03:10 recording (npc 2930819 twice at -26, 878 m, gone 9 s later). Thread-safe:
/// written on the capture thread, read by the map's timer.
/// </summary>
public sealed class Aion2Guards
{
    /// <summary>A guard never removed (the player left the map without the game saying so) is dropped after this.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private readonly Dictionary<int, (float X, float Y, DateTime At)> _live = new();

    /// <summary>The NPC ids the client names "Guard" (the map only draws them on the Asmodian maps, where they are the
    /// enemy's: at home the same militia guards are friendly - Stacy, 2026-10-17).</summary>
    public static bool IsGuard(int npcId) => npcId is >= 2930813 and <= 2930820 or >= 2931239 and <= 2931242 or 2939024
        or 2950012 or 2950139 or 2950619 or 2950727 or 2950739 or 2950744 or 2950839 or 2950973 or 2951099 or 2951100
        or 2959287 or 2959288 or >= 2989445 and <= 2989448;

    /// <summary>Whether a map is the enemy's for an Elyos player (Altgard, Morheim): only there are the guards drawn.</summary>
    public static bool IsEnemyMap(string? mapKey) => mapKey is not null && mapKey.StartsWith("World_D", StringComparison.Ordinal);

    public void Clear()
    {
        lock (_gate)
        {
            _live.Clear();
        }
    }

    public void NoteSpawn(int entityId, int npcId, float x, float y, DateTime at)
    {
        if (!IsGuard(npcId) || !float.IsFinite(x) || !float.IsFinite(y) || Math.Abs(x) >= 1_000_000 || Math.Abs(y) >= 1_000_000)
        {
            return;
        }

        lock (_gate)
        {
            _live[entityId] = (x, y, at);
        }
    }

    /// <summary>Any NPC is gone (0x4236): forgets it if it was a guard.</summary>
    public void NoteRemoved(int entityId)
    {
        lock (_gate)
        {
            _live.Remove(entityId);
        }
    }

    /// <summary>Every guard announced and not yet gone, as of <paramref name="now"/> (capture time).</summary>
    public IReadOnlyList<(float X, float Y)> Live(DateTime now)
    {
        lock (_gate)
        {
            foreach (int stale in _live.Where(g => now - g.Value.At > MaxAge).Select(g => g.Key).ToList())
            {
                _live.Remove(stale);
            }

            return _live.Values.Select(g => (g.X, g.Y)).ToList();
        }
    }
}
