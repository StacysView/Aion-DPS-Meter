using System.IO;

namespace AionDPS.Ui;

/// <summary>
/// The player's pet choices shared by every pet view: the checked (pinned) pets and the followed farm zone, kept in
/// the settings file (<see cref="MeterSettings.PetPins"/>, <see cref="MeterSettings.PetFarmZone"/>). The Pets window,
/// the list panel over the game (a click on a line checks it) and the Familiers page of the settings all write them
/// here and follow <see cref="Changed"/>, so they stay in sync. UI thread only.
/// </summary>
public static class PetChoices
{
    /// <summary>The pins or the followed zone changed (and were saved).</summary>
    public static event Action<IReadOnlyCollection<int>, string?>? Changed;

    /// <summary>The pins with <paramref name="petId"/> checked or unchecked (no WPF: tested by the selftest).</summary>
    public static HashSet<int> Toggled(IEnumerable<int> pins, int petId)
    {
        var next = pins.ToHashSet();
        if (!next.Remove(petId))
        {
            next.Add(petId);
        }

        return next;
    }

    /// <summary>Writes the pins and the zone into the settings file, keeping every other setting as it is on disk (a
    /// locked file loses only this save: the choice stays for the session), then tells every view.</summary>
    public static void Save(IEnumerable<int> pins, string? farmZone)
    {
        var sorted = pins.Distinct().OrderBy(i => i).ToList();
        try
        {
            MeterSettings onDisk = MeterSettings.Load();
            onDisk.PetPins = sorted;
            onDisk.PetFarmZone = farmZone;
            onDisk.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        Changed?.Invoke(sorted, farmZone);
    }
}
