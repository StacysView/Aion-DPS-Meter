namespace AionDPS.Combat;

/// <summary>
/// Keeps a running per-source damage tally over the DamageEvents the Chat.log parser produces,
/// and renders the leaderboard the console mode prints. Deliberately knows nothing about where
/// the events came from: it predates the chat-log path and survived the removal of the packet
/// one unchanged, which is the whole point of DamageEvent sitting between them.
/// </summary>
public sealed class LiveAggregator
{
    private readonly List<DamageEvent> _events = new();

    public IReadOnlyList<DamageEvent> Events => _events;

    /// <summary>
    /// Feeds parsed DamageEvents in -- AionDPS.ChatLog.ChatLogParser produces them directly.
    /// </summary>
    public void IngestEvents(IEnumerable<DamageEvent> events) => _events.AddRange(events);

    /// <summary>
    /// Discards all events for a fresh session. Previously missing on purpose-turned-oversight:
    /// the GUI's "Clear" button used to only clear its own row list, leaving the underlying events
    /// in place -- so DpsCalculator kept averaging in pre-clear damage into any "new" numbers
    /// instead of starting from zero, invisibly, since the row list looked empty right after.
    /// </summary>
    public void Clear() => _events.Clear();

    /// <summary>
    /// Credits a summon's earlier hits to its owner, once the owner is known: the class-skill hits
    /// (a summon's attacks are its class's skills) and the summon attacks (a spirit's basic attack,
    /// whose id names no class) <paramref name="summon"/> dealt, damage or heal. Other events under
    /// that id - a monster that held it before - stay as they are.
    /// </summary>
    public int Reattribute(int summon, int owner)
    {
        int changed = 0;
        for (int i = 0; i < _events.Count; i++)
        {
            DamageEvent ev = _events[i];
            if (ev.SourceObjectId == summon && ev.SkillId != 0
                && (Aion2.Protocol.Aion2SkillNames.ClassOf(ev.SkillId) is not null || Aion2.Protocol.Aion2SkillNames.IsSummonAttack(ev.SkillId)))
            {
                _events[i] = ev with { SourceObjectId = owner };
                changed++;
            }
        }

        return changed;
    }

    /// <summary>One-line-per-source leaderboard for the console dump: total damage and wall-clock
    /// "ALL" DPS since the first hit seen for that source (see DpsCalculator remarks on why this is
    /// the ambiguous, gap-inclusive variant rather than iDPS). <paramref name="nameResolver"/> lets
    /// the chat-log path show real names (it has them for free from Chat.log) instead of the raw
    /// synthetic/hex object id the network path is stuck with until SM_GROUP_MEMBER_INFO is wired up.</summary>
    public string Summarize(Func<int, string?>? nameResolver = null)
    {
        // Found by terminal_windows against a real, heal-bearing Chat.log session: this used to
        // group ALL events (heals included) while the Dps column below already filtered heals out
        // via DpsCalculator.HitsBy -- so a pure healer showed up as "Potion: 3526 dmg (n/a DPS)",
        // a damage total with no rate to match it, because the total and the rate were silently
        // computed over two different event sets.
        var damageEvents = _events.Where(e => !e.IsHeal).ToList();
        if (damageEvents.Count == 0)
        {
            return "(no attributable damage yet)";
        }

        var lines = damageEvents
            .GroupBy(e => e.SourceObjectId)
            .Select(g => (SourceId: g.Key, Total: g.Sum(e => e.Amount), Dps: DpsCalculator.AllDpsWallClock(damageEvents, g.Key)))
            .OrderByDescending(x => x.Total)
            .Select(x => $"{nameResolver?.Invoke(x.SourceId) ?? $"0x{x.SourceId:X8}"}: {x.Total} dmg ({(x.Dps is double d ? d.ToString("F0") : "n/a")} DPS)");

        return string.Join(" | ", lines);
    }
}
