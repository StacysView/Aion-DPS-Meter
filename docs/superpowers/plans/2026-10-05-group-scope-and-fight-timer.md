# Group scope without a roster, and a fight timer that follows the rows - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Group shows only the local player until a roster is read, and the fight on screen (timer, rate span, automatic reset, history span) only counts events of the shown players.

**Architecture:** One predicate in `MainWindow` (`InShownFight(DamageEvent)`: a shown player is the source or the target) feeds the overlay timer, `filteredSpan` and `StartsNewFight`; `IsInScope` loses the no-roster fallback for Group; `RecentFights.Describe` measures the span on the shown players' damage.

**Tech Stack:** C# / .NET 10 WPF, self-test (`dotnet "Aion DPS.dll" selftest`), headless replay harness.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-10-05-group-scope-and-fight-timer-design.md`.
- "All" scope and PvP behave exactly as before.
- Taken mode's `IsTeammate` keeps its no-roster fallback.
- Build `dotnet build Client -c Release -v q -nologo`; self-test ends with `ALL CHECKS PASSED`; never launch the app without arguments.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: History span on the shown players' damage

**Files:** Modify `Client/History/RecentFights.cs` (`Describe`), `Client/Combat/SelfCheckHistory.cs` (`RunRecentFightsScenario`).

- [ ] **Step 1: Failing test** - in `RunRecentFightsScenario`, after the stranger's hit at t+5 add `events.Add(new DamageEvent(t.AddSeconds(-30), Stranger, Boss, 5000, IsHeal: false));` (the existing checks - duration 20 s, DPS 1050 - then fail: the stranger stretches the span to 50 s).
- [ ] **Step 2: Run the self-test** - expected: `shown players with damage/DPS/healing/taken: False`.
- [ ] **Step 3: Implement** - in `Describe`, take `start`/`end` from `damage.Where(ev => isShown(ev.SourceObjectId))` (null when there is none); DPS keeps dividing by that span.
- [ ] **Step 4: Self-test passes; commit** `History: a fight's span is the shown players' own`.

### Task 2: Group without a roster is the local player alone

**Files:** Modify `Client/Ui/MainWindow.xaml.cs` (`IsInScope`).

- [ ] **Step 1: Implement** - in `IsInScope`, replace the block `if (_scope == MeterScope.Group && !RosterKnown(directory)) { return _alongsideIds.Contains(sourceId); }` and its comment by nothing (the final `return IsGroupMember(...) || (_scope == MeterScope.Raid && _alongsideIds.Contains(sourceId));` already answers "only the local player" without a roster); update the method's summary.
- [ ] **Step 2: Build, self-test, commit** `Group scope without a roster: the local player alone`.

### Task 3: The fight on screen follows the shown players

**Files:** Modify `Client/Ui/MainWindow.xaml.cs` (`RefreshRows` `filteredSpan`, `UpdateCompactOverlay` timer, `StartsNewFight`).

- [ ] **Step 1: Predicate** - add next to `IsInScope`:

```csharp
    /// <summary>An event of the fight on screen: a shown player (the scope's) dealt or took it. In
    /// "All" that is every player's; in Group a stranger hitting a monster nearby is not part of it.</summary>
    private bool InShownFight(DamageEvent ev) =>
        (IsPlayerName(ev.SourceObjectId) && IsInScope(ev.SourceObjectId))
        || (IsPlayerName(ev.TargetObjectId) && IsInScope(ev.TargetObjectId))
        || _source?.Entities.IsLocalPlayer(ev.SourceObjectId) == true;
```

- [ ] **Step 2: Span** - `filteredSpan` from `filtered.Where(InShownFight)`.
- [ ] **Step 3: Timer** - in `UpdateCompactOverlay`, the `OverlayTimeText` span from `shownHits.Where(InShownFight)`.
- [ ] **Step 4: Automatic reset** - in `StartsNewFight`, both the batch's first event and the last recorded one only among `!IsHeal && InShownFight(ev)`.
- [ ] **Step 5: Build, self-test, replay the 22:33 dummy capture and the 00:22 Auldor capture through the headless window (rows per scope, overlay timer), commit** `The fight on screen follows the shown players: timer, rate span, automatic reset`.

### Task 4: Release

- [ ] Smoke test, `<Version>0.9.50</Version>`, commit, push `HEAD:main` and tag `v0.9.50` to `fork`, wait for the workflow.
