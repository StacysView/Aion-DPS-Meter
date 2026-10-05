using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>
/// The last overlay fights (History/RecentFights): pick one to see its players, double-click a
/// player for their skills in that fight. The live overlay keeps running beside it. The store is
/// the caller's - opened once by MainWindow, which files the fights.
/// </summary>
public partial class FightHistoryWindow : Window
{
    private readonly FightStore _store;
    private FightDetail? _shown;

    /// <summary>A player's equipment by name (and whether it is the local player), for the details
    /// window - MainWindow's, which owns the packet source.</summary>
    public Func<string, bool, Aion2.Aion2InspectedPlayer?>? GearNamed { get; init; }

    public FightHistoryWindow(FightStore store)
    {
        InitializeComponent();
        // WPF formats bound dates and numbers in en-US unless told otherwise: "10/4/2026 10:39 AM".
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag);
        _store = store;
        Refresh();
    }

    public void Refresh()
    {
        List<FightSummary> fights = _store.Query(null, RecentFights.Keep);
        FightsGrid.ItemsSource = fights;
        CountText.Text = string.Format(LocalizationManager.Instance["History.Count"], fights.Count);
        ParticipantsGrid.ItemsSource = null;
        _shown = null;
    }

    /// <summary>One line of the players' grid, with the share of the fight's damage.</summary>
    private sealed record ParticipantRow(string Name, string ClassName, long Damage, string DpsDisplay, string Share, long Healing, long DamageTaken);

    private void OnFightSelected(object sender, SelectionChangedEventArgs e)
    {
        _shown = FightsGrid.SelectedItem is FightSummary summary ? _store.Load(summary.Id) : null;
        long total = _shown?.Participants.Sum(p => p.Damage) ?? 0;
        ParticipantsGrid.ItemsSource = _shown?.Participants
            .Select(p => new ParticipantRow(p.Name, p.ClassName, p.Damage, p.DpsDisplay,
                total > 0 ? (100.0 * p.Damage / total).ToString("F1") + " %" : "", p.Healing, p.DamageTaken))
            .ToList();
    }

    /// <summary>The player's hits in the stored fight, through the same skills window as the
    /// overlay's (ids are the fight's own; names come from its id-to-name table).</summary>
    private void OnParticipantDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_shown is not FightDetail fight || ParticipantsGrid.SelectedItem is not ParticipantRow row)
        {
            return;
        }

        var ids = fight.Names.Where(kv => kv.Value == row.Name).Select(kv => kv.Key).ToHashSet();
        var hits = fight.Events.Where(ev => !ev.IsHeal && ids.Contains(ev.SourceObjectId) && !ids.Contains(ev.TargetObjectId)).ToList();
        FightParticipant? who = fight.Participants.FirstOrDefault(p => p.Name == row.Name);
        new PlayerDetailsWindow(row.Name, row.ClassName, who?.Faction ?? "", who?.IsSelf == true, hits,
            id => fight.Names.GetValueOrDefault(id), exactCrits: true, gear: GearNamed?.Invoke(row.Name, who?.IsSelf == true))
        {
            Owner = this,
            Topmost = Topmost,
        }.Show();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
