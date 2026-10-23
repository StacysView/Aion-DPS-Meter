using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>
/// The last overlay fights (History/RecentFights): pick one to see its players, click a player for
/// their skills in that fight. The live overlay keeps running beside it. The store is the
/// caller's - opened once by MainWindow, which files the fights.
/// </summary>
public partial class FightHistoryWindow : Window
{
    private readonly FightStore _store;
    private FightDetail? _shown;

    public FightHistoryWindow(FightStore store)
    {
        InitializeComponent();
        // WPF formats bound dates and numbers in en-US unless told otherwise: "10/4/2026 10:39 AM".
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        ThemedChrome.Apply(this);
        _store = store;
        Refresh();
    }

    /// <summary>A fight in the list: its target, when and how long, the group's damage.</summary>
    private sealed record FightItem(FightSummary Summary, string Target, string When, string Damage);

    /// <summary>One player's line: rank by damage, the bar against the best, the numbers.</summary>
    private sealed record ParticipantRow(int Rank, string Name, string ClassName, string ClassLabel, bool IsSelf,
        double BarPercent, Brush Bar, long Damage, double? Dps, string Share, long Healing, long DamageTaken);

    /// <summary>The list again, the latest fight chosen - an empty right side said nothing.</summary>
    public void Refresh()
    {
        List<FightSummary> fights = _store.Query(null, RecentFights.Keep);
        DateTime today = DateTime.Today;
        FightsList.ItemsSource = fights.Select(f => new FightItem(f, f.TargetName,
            (f.StartedAt.Date == today ? f.StartedAt.ToString("t") : f.StartedAt.ToString("d MMM") + " " + f.StartedAt.ToString("t")) + " · " + f.DurationDisplay,
            PlayerRow.Compact(f.TotalDamage))).ToList();
        CountText.Text = string.Format(LocalizationManager.Instance["History.Count"], fights.Count);
        EmptyText.Visibility = fights.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FightsList.SelectedIndex = fights.Count > 0 ? 0 : -1;
        if (fights.Count == 0)
        {
            ShowFight(null);
        }
    }

    private void OnFightSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Ctrl+click adds a second fight; the right side keeps showing the first one picked.
        CompareButton.IsEnabled = FightsList.SelectedItems.Count == 2;
        ShowFight(FightsList.SelectedItem is FightItem item ? _store.Load(item.Summary.Id) : null);
    }

    /// <summary>The two picked fights in the dummy lab (their skills and skill levels side by side).</summary>
    private void OnCompareClicked(object sender, RoutedEventArgs e)
    {
        if (FightsList.SelectedItems.Count != 2)
        {
            return;
        }

        FightDetail? first = _store.Load(((FightItem)FightsList.SelectedItems[0]!).Summary.Id);
        FightDetail? second = _store.Load(((FightItem)FightsList.SelectedItems[1]!).Summary.Id);
        if (first is null || second is null)
        {
            return;
        }

        new DummyLabWindow(first, second)
        {
            Owner = this,
            Topmost = Topmost,
        }.Show();
    }

    private void ShowFight(FightDetail? fight)
    {
        _shown = fight;
        if (fight is null)
        {
            FightTitle.Text = "";
            FightSubText.Text = "";
            ParticipantsList.ItemsSource = null;
            return;
        }

        var loc = LocalizationManager.Instance;
        FightSummary summary = fight.Summary;
        FightTitle.Text = summary.TargetName;
        DateTime at = summary.StartedAt;
        FightSubText.Text = string.Format(loc["History.Summary"], at.ToString("dddd d MMMM") + " " + at.ToString("t"), summary.DurationDisplay,
            fight.Participants.Count, PlayerRow.Compact(fight.Participants.Sum(p => p.Damage)));

        long total = fight.Participants.Sum(p => p.Damage);
        long best = fight.Participants.Count > 0 ? fight.Participants.Max(p => p.Damage) : 0;
        ParticipantsList.ItemsSource = fight.Participants
            .OrderByDescending(p => p.Damage)
            .Select((p, i) => new ParticipantRow(i + 1, p.Name, p.ClassName, ClassLabel(p.ClassName), p.IsSelf,
                best > 0 ? 100.0 * p.Damage / best : 0, BarBrush(p.ClassName), p.Damage,
                p.Dps,
                total > 0 ? (100.0 * p.Damage / total).ToString("F1") + " %" : "-", p.Healing, p.DamageTaken))
            .ToList();
    }

    /// <summary>The class in the meter's language ("Élémentaliste"), its own name when untranslated.</summary>
    private static string ClassLabel(string className)
    {
        string key = $"Class.{className}";
        string named = LocalizationManager.Instance[key];
        return named != key ? named : className;
    }

    /// <summary>The class colour, half see-through, as under the skills of the details window.</summary>
    private static Brush BarBrush(string className)
    {
        Color c = ClassColors.For(className).Color;
        var brush = new LinearGradientBrush(Color.FromArgb(0x8C, c.R, c.G, c.B), Color.FromArgb(0x59, c.R, c.G, c.B), 0);
        brush.Freeze();
        return brush;
    }

    /// <summary>The player's hits in the stored fight, through the same skills window as the
    /// overlay's (ids are the fight's own; names come from its id-to-name table).</summary>
    private void OnParticipantClicked(object sender, MouseButtonEventArgs e)
    {
        if (_shown is not FightDetail fight || (sender as FrameworkElement)?.DataContext is not ParticipantRow row)
        {
            return;
        }

        var ids = fight.Names.Where(kv => kv.Value == row.Name).Select(kv => kv.Key).ToHashSet();
        var hits = fight.Events.Where(ev => !ev.IsHeal && ids.Contains(ev.SourceObjectId) && !ids.Contains(ev.TargetObjectId)).ToList();
        FightParticipant? who = fight.Participants.FirstOrDefault(p => p.Name == row.Name);
        var data = new PlayerDetailsData(row.Name, row.ClassName, who?.Faction ?? "", who?.IsSelf == true, row.Rank, hits,
            fight.Participants.Sum(p => p.Damage), (fight.Summary.StartedAt, fight.Summary.EndedAt), id => fight.Names.GetValueOrDefault(id));
        new PlayerDetailsWindow(data)
        {
            Owner = this,
            Topmost = Topmost,
        }.Show();
    }
}
