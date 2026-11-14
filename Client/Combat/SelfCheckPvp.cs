using System.IO;
using AionDPS.Aion2;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The duel score and the PvP watch (Stacy, 2026-11-14).</summary>
public static class SelfCheckPvp
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] PvP: duels and the Abyss watch:");
        var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var duels = new Aion2Duels();
        for (int i = 0; i < 5; i++)
        {
            duels.Note("Jeane", killedMe: true, now);
        }

        duels.Note("Jeane", killedMe: false, now);
        duels.Note("Jeane", killedMe: false, now);
        DuelRecord? jeane = duels.Of("Jeane", now.AddDays(1));
        bool score = jeane is { KilledMe: 5, IKilled: 2, Side: -1 } && duels.Of("Nobody", now) is null && duels.Totals(now) == (5, 2);
        bool forgotten = duels.Of("Jeane", now.AddDays(15)) is null && duels.Totals(now.AddDays(15)) == (0, 0);
        bool lead = new DuelRecord(1, 3, now).Side == 1 && new DuelRecord(2, 2, now).Side == 0;
        string file = Path.Combine(Path.GetTempPath(), "aiondps-duels-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var kept = new Aion2Duels(file);
            kept.Note("Kept", killedMe: true, DateTime.UtcNow);
            bool persisted = new Aion2Duels(file).Of("Kept", DateTime.UtcNow) is { KilledMe: 1 };
            File.WriteAllText(file, "{ not json");
            bool damaged = new Aion2Duels(file).Of("Kept", DateTime.UtcNow) is null;
            score &= persisted && damaged;
        }
        finally
        {
            File.Delete(file);
        }

        var directory = new Aion2EntityDirectory();
        var watch = new PvpWatch();
        bool outside = watch.Poll(directory, 1010, now, true, true).Count == 0 && !watch.Active;
        bool entry = watch.Poll(directory, 20, now, true, true).Count == 0 && watch.Active; // coming in: nothing is news
        bool abyss = PvpWatch.IsAbyss(20) && PvpWatch.IsAbyss(22) && PvpWatch.IsAbyss(23) && !PvpWatch.IsAbyss(1010);
        bool left = watch.Poll(directory, 1010, now, true, true).Count == 0 && !watch.Active;

        // "Enemy near": one sound for the first enemy after an empty second, none for those who join him, again after he is gone for a second.
        var view = new Aion2EntityDirectory();
        var near = new PvpWatch();
        void Enemy(int id, string name)
        {
            view.Register(id, name);
            view.NoteAppeared(id, 1, now);
            view.NoteHostile(id, true);
        }

        int Near(double seconds) => near.Poll(view, 20, now.AddSeconds(seconds), true, false).Count(a => a.Kind == PvpAlertKind.EnemyNear);
        Near(0); // comes in, nobody there
        Enemy(101, "Early");
        bool tooSoon = Near(0.5) == 0; // not yet empty for a second: not armed
        view.NotePlayerGone(101);
        Near(1);
        bool quietEmpty = Near(2.25) == 0;
        Enemy(102, "First");
        bool rings = Near(2.5) == 1;
        Enemy(103, "Second");
        bool silentJoin = Near(2.75) == 0 && Near(10) == 0;
        view.NotePlayerGone(102);
        bool oneLeft = Near(11) == 0; // 103 is still there
        view.NotePlayerGone(103);
        Near(11.25);
        Enemy(104, "Quick");
        bool shortGap = Near(11.75) == 0; // empty for half a second only
        view.NotePlayerGone(104);
        Near(12);
        Near(13.25);
        Enemy(105, "Again");
        bool again = Near(13.5) == 1;
        view.Register(106, "Friend");
        view.NoteAppeared(106, 1, now);
        view.NoteHostile(106, false);
        view.NotePlayerGone(105);
        Near(14);
        bool friend = Near(16) == 0; // a friend in view is nobody
        bool nearOk = tooSoon && quietEmpty && rings && silentJoin && oneLeft && shortGap && again && friend;
        Console.WriteLine($"  -> enemy near: one sound for the first after an empty second {rings}, none for those who join {silentJoin && oneLeft}, not after a shorter gap {tooSoon && shortGap}, again once empty {again}, friends do not count {friend}");
        Console.WriteLine($"  -> score 5-2 (he leads), forgotten after 14 days, kept in a file, damaged file ignored: {score && forgotten}; the lead: {lead}; watch off outside the Abyss {outside}, quiet on entry {entry}, Abyss zones {abyss}, off again {left}");
        return score && forgotten && lead && outside && entry && abyss && left && nearOk;
    }
}
