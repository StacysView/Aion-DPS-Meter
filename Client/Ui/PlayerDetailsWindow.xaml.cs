using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>The three breakdowns of a player: what they dealt, what they healed, what they took.</summary>
public enum DetailKind { Damage, Heal, Taken }

/// <summary>One skill of a player, as the details table shows it. <see cref="SharePercent"/> is
/// its share of the player's own total, <see cref="BarPercent"/> its size against their best skill
/// (the bar under its name), <see cref="Rate"/> its damage (or healing) per second of the fight.</summary>
public sealed record SkillRow(string Skill, int Hits, int CritHits, double CritRate, long Total, long Min, long Max, long Average,
    double SharePercent, double BarPercent, double Rate, string TotalText, string ShareText, string RateText, string CritText,
    Brush Bar, ImageSource? Icon = null);

/// <summary>Everything the details window shows of one player in one fight.</summary>
/// <param name="Events">What the tabs break down: the player's damage, their heals with
/// <paramref name="Heals"/>, the hits they took with <paramref name="Taken"/>.</param>
/// <param name="GroupTotal">The shown players' total of the same kind - the contribution tile.</param>
/// <param name="Span">The fight's first and last moment when known; else the events' own.</param>
/// <param name="Deaths">Taken: their deaths, with the blow that did each.</param>
public sealed record PlayerDetailsData(
    string Name, string ClassName, string Faction, bool IsLocalPlayer, int Rank,
    IReadOnlyList<DamageEvent> Events, long GroupTotal, (DateTime Start, DateTime End)? Span, Func<int, string?> NameOf,
    bool Heals = false, int? BossId = null, bool Taken = false, IReadOnlyList<Death>? Deaths = null);

/// <summary>
/// One player's fight, in the overlay's "modern classic" look (Stacy, 2026-10-16, after QuestLog's
/// details panel): name, rank and class; four tiles - DPS, damage, contribution, duration; one table (Skills and Hits merged, 2026-10-17) -
/// Skills (damage, share, DPS, crits, over a bar in the class colour) and Hits (count, crits, min,
/// max, average). In Taken mode the lines are the attackers and their attacks, and the deaths are
/// listed under. Opened from the overlay and from the history. A snapshot: it does not follow the
/// fight, a table reshuffling under the cursor is unreadable.
/// </summary>
public partial class PlayerDetailsWindow : Window
{
    // Each column's own header text; the sorted one gets an arrow behind it.
    private readonly Dictionary<DataGridColumn, string> _headers = new();

    private readonly Func<DetailKind, PlayerDetailsData?>? _provider;
    private readonly object? _rateLabel0, _amountLabel0, _amountHeader0, _rateHeader0;
    private readonly Dictionary<DetailKind, Border> _switch = new();

    /// <param name="provider">Supplies the same player's data for another kind (the switch in the header Damage / Healing / Damage taken); null, no switch.</param>
    public PlayerDetailsWindow(PlayerDetailsData data, Func<DetailKind, PlayerDetailsData?>? provider = null)
    {
        InitializeComponent();
        // WPF formats bound numbers in en-US unless told otherwise ("12,705", "40.0%").
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        ThemedChrome.Apply(this);
        _provider = provider;
        _rateLabel0 = RateTileLabel.Text;
        _amountLabel0 = AmountTileLabel.Text;
        _amountHeader0 = AmountColumn.Header;
        _rateHeader0 = RateColumn.Header;
        BuildModeSwitch();
        Load(data);
    }

    private static DetailKind KindOf(PlayerDetailsData data) => data.Taken ? DetailKind.Taken : data.Heals ? DetailKind.Heal : DetailKind.Damage;

    private void BuildModeSwitch()
    {
        if (_provider is null)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        foreach ((DetailKind kind, string key) in new[] { (DetailKind.Damage, "Main.Overlay.ModeDamage"), (DetailKind.Heal, "Main.Overlay.ModeHeal"), (DetailKind.Taken, "Main.Overlay.ModeTaken") })
        {
            var text = new TextBlock { Text = loc[key], FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var tab = new Border { Child = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(2, 0, 0, 0), CornerRadius = new CornerRadius(6), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
            tab.MouseLeftButtonUp += (_, _) =>
            {
                if (_provider(kind) is PlayerDetailsData next)
                {
                    Load(next);
                    UpdateModeSwitch(kind);
                }
            };
            _switch[kind] = tab;
            ModeSwitch.Children.Add(tab);
        }

        ModeSwitch.Visibility = Visibility.Visible;
    }

    private void UpdateModeSwitch(DetailKind current)
    {
        foreach ((DetailKind kind, Border tab) in _switch)
        {
            bool on = kind == current;
            tab.SetResourceReference(Border.BackgroundProperty, on ? "Brush.BrandSelect" : "Brush.Button");
            ((TextBlock)tab.Child).SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.BrandText" : "Brush.Muted");
        }
    }

    /// <summary>Fills the window from one kind's data (again when the switch is used: the labels and headers go back to what the damage view has first).</summary>
    private void Load(PlayerDetailsData data)
    {
        var loc = LocalizationManager.Instance;
        string monster = loc["Details.Monster"];
        RateTileLabel.Text = (string?)_rateLabel0 ?? "DPS";
        AmountTileLabel.Text = (string?)_amountLabel0 ?? "";
        AmountColumn.Header = _amountHeader0;
        RateColumn.Header = _rateHeader0;
        _headers.Clear();
        foreach (DataGridColumn column in SkillsGrid.Columns)
        {
            column.SortDirection = null;
        }

        UpdateModeSwitch(KindOf(data));

        // Header.
        HeaderText.Text = data.Name;
        MeBadge.Visibility = data.IsLocalPlayer ? Visibility.Visible : Visibility.Collapsed;
        ClassIcon.Source = new ClassIconConverter().Convert(data.ClassName, typeof(ImageSource), null, CultureInfo.CurrentCulture) as ImageSource;
        string className = loc[$"Class.{data.ClassName}"] is string named && named != $"Class.{data.ClassName}" ? named : data.ClassName;
        SubText.Text = string.Join(" · ", new[] { data.Rank > 0 ? $"#{data.Rank}" : null, className }.Where(part => !string.IsNullOrEmpty(part)));

        // Taken: one line per attacker and attack.
        var events = data.Taken
            ? data.Events.Select(e => e with { Skill = $"{data.NameOf(e.SourceObjectId) ?? monster} : {e.Skill ?? "?"}", SkillId = 0 }).ToList()
            : data.Events.ToList();
        bool heals = data.Heals && !data.Taken;
        var shown = events.Where(e => e.IsHeal == heals).ToList();

        // The tiles: over the fight's span, else from the first to the last event.
        long total = shown.Sum(e => e.Amount);
        TimeSpan fight = data.Span is (DateTime start, DateTime end) && end > start ? end - start
            : shown.Count > 1 ? shown.Max(e => e.Timestamp) - shown.Min(e => e.Timestamp) : TimeSpan.Zero;
        double seconds = fight.TotalSeconds;
        if (heals)
        {
            AmountTileLabel.Text = loc["Details.Tile.Heal"];
            RateTileLabel.Text = loc["Details.Tile.Hps"];
            AmountColumn.Header = loc["Details.Col.Heal"];
            RateColumn.Header = loc["Details.Tile.Hps"];
        }
        else if (data.Taken)
        {
            AmountTileLabel.Text = loc["Details.Tile.Taken"];
            RateTileLabel.Text = "DTPS";
            RateColumn.Header = "DTPS";
        }

        RateTileText.Text = seconds >= 1 ? PlayerRow.Compact((long)Math.Round(total / seconds)) : "-";
        AmountTileText.Text = PlayerRow.Compact(total);
        ShareTileText.Text = data.GroupTotal > 0 ? (100.0 * total / data.GroupTotal).ToString("0.0", CultureInfo.CurrentCulture) + " %" : "-";
        DurationTileText.Text = seconds >= 1 ? Clock(fight) : "-";

        // The table: one line per skill.
        Brush bar = BarBrush(data.ClassName);
        var breakdown = SkillBreakdown.For(shown, heals);
        long best = breakdown.Count > 0 ? breakdown.Max(u => u.Total) : 0;
        var rows = breakdown.Select(u =>
        {
            double share = total > 0 ? 100.0 * u.Total / total : 0;
            double rate = seconds >= 1 ? u.Total / seconds : 0;
            double crit = u.Hits > 0 ? 100.0 * u.CritHits / u.Hits : 0;
            return new SkillRow(Aion2.Protocol.Aion2SkillNames.Display(u.Skill), u.Hits, u.CritHits, crit, u.Total, u.Min, u.Max,
                (long)Math.Round((double)u.Total / Math.Max(1, u.Hits)), share, best > 0 ? 100.0 * u.Total / best : 0, rate,
                PlayerRow.Compact(u.Total), share.ToString("0.0", CultureInfo.CurrentCulture) + " %",
                seconds >= 1 ? PlayerRow.Compact((long)Math.Round(rate)) : "-",
                heals ? "-" : crit.ToString("0", CultureInfo.CurrentCulture) + " %", bar, IconFor(u.SkillId));
        }).ToList();
        SkillsGrid.ItemsSource = rows;

        // A boss fight counts the adds too: how this player's damage split between the two.
        if (!heals && !data.Taken && data.BossId is int boss && total > 0)
        {
            long onBoss = shown.Where(e => e.TargetObjectId == boss).Sum(e => e.Amount);
            string share(long part) => (100.0 * part / total).ToString("0", CultureInfo.CurrentCulture) + " %";
            SubText.Text += " · " + string.Format(loc["Details.BossAdds"], share(onBoss), share(total - onBoss));
        }

        // Under the tables: where the crits come from, or in Taken mode the deaths and their blows.
        NoteText.Text = data.Taken
            ? data.Deaths is { Count: > 0 } deaths
                ? string.Join("\n", deaths.Select(d => "☠ " + d.At.ToLocalTime().ToString("HH:mm:ss") + "  " + (d.KillingBlow is DamageEvent blow
                    ? string.Format(loc["Details.KilledBy"], data.NameOf(blow.SourceObjectId) ?? monster, blow.Skill ?? "?", blow.Amount.ToString("N0"))
                    : loc["Details.Died"])))
                : loc["Details.NoDeath"]
            : heals ? "" : loc["Details.CritNote"];
        foreach (DataGrid grid in new[] { SkillsGrid })
        {
            foreach (DataGridColumn column in grid.Columns)
            {
                _headers[column] = column.Header as string ?? "";
            }
        }

        if (SkillsGrid.Columns.FirstOrDefault(c => c.SortMemberPath == "Total") is DataGridColumn totalColumn)
        {
            ApplySort(SkillsGrid, totalColumn, ListSortDirection.Descending);
        }
    }

    /// <summary>"3:11", or "1:02:05" past an hour.</summary>
    private static string Clock(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : $"{(int)time.TotalMinutes}:{time.Seconds:D2}";

    /// <summary>The class colour, half see-through, for the bars under the skills.</summary>
    private static Brush BarBrush(string className)
    {
        Color c = ClassColors.For(className).Color;
        var brush = new LinearGradientBrush(Color.FromArgb(0x8C, c.R, c.G, c.B), Color.FromArgb(0x59, c.R, c.G, c.B), 0);
        brush.Freeze();
        return brush;
    }

    /// <summary>A click on a header sorts by it: numbers largest first, the skill A to Z; a second
    /// click reverses. The arrow shows which column and which way.</summary>
    private void OnGridSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        ListSortDirection direction = e.Column.SortDirection switch
        {
            ListSortDirection.Descending => ListSortDirection.Ascending,
            ListSortDirection.Ascending => ListSortDirection.Descending,
            _ => e.Column.SortMemberPath == "Skill" ? ListSortDirection.Ascending : ListSortDirection.Descending,
        };
        ApplySort((DataGrid)sender, e.Column, direction);
    }

    private void ApplySort(DataGrid grid, DataGridColumn column, ListSortDirection direction)
    {
        if (string.IsNullOrEmpty(column.SortMemberPath) || grid.ItemsSource is null)
        {
            return;
        }

        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(grid.ItemsSource);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(column.SortMemberPath, direction));
        foreach (DataGridColumn other in grid.Columns)
        {
            other.SortDirection = null;
            other.Header = _headers.GetValueOrDefault(other, other.Header as string ?? "");
        }

        column.SortDirection = direction;
        column.Header = _headers.GetValueOrDefault(column, "") + (direction == ListSortDirection.Descending ? " ▼" : " ▲");
    }

    /// <summary>The skill's icon (see Aion2.Protocol.Aion2SkillIcons), decoded once at its small
    /// size; null when the skill has none.</summary>
    private static ImageSource? IconFor(int skillId)
    {
        if (Aion2.Protocol.Aion2SkillIcons.PathFor(skillId) is not string path)
        {
            return null;
        }

        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 48;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException)
        {
            return null;
        }
    }
}
