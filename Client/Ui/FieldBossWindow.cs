using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Aion2;
using AionDPS.Schedule;

namespace AionDPS.Ui;

/// <summary>
/// The field bosses of the world map: for each map where the game's boss list was seen, every boss with
/// "là" or the time it comes back; the other maps are folded under their name. A star marks the bosses the
/// overlay's footer and reminder follow (MeterSettings.WatchedFieldBosses). Code-built, in the look of
/// FightHistoryWindow. Texts are updated in place every second; the tree is rebuilt only when the order or
/// the known / unknown state of a map changes. The list reader and data are SkeeveAN's (MIT).
/// </summary>
public sealed class FieldBossWindow : Window
{
    private static readonly Brush Muted = Frozen(0x8F, 0xA3, 0xB3);
    private static readonly Brush Quiet = Frozen(0xB4, 0xC3, 0xCF);
    private static readonly Brush VioletText = Frozen(0xB7, 0xA4, 0xFF);
    private static readonly Brush Green = Frozen(0x5B, 0xD7, 0x5B); // the overlay's EventOpenBrush
    private static readonly Brush CardBack = Frozen(0x1A, 0xFF, 0xFF, 0xFF);

    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroll;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly HashSet<string> _opened = [];
    private readonly List<Action<DateTime>> _updaters = [];
    private HashSet<int> _watched = [];
    private string _signature = "";

    public FieldBossWindow()
    {
        Title = LocalizationManager.Instance["FieldBosses.Title"];
        Width = 460;
        Height = 620;
        MinWidth = 380;
        MinHeight = 320;
        FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Foreground = Frozen(0xEA, 0xF0, 0xF5);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        System.Windows.Documents.Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Padding = new Thickness(0, 0, 4, 0) };
        var root = new Grid
        {
            Background = new LinearGradientBrush(Color.FromRgb(0x14, 0x1C, 0x24), Color.FromRgb(0x0B, 0x10, 0x16), 90),
        };
        root.Children.Add(_scroll);
        Content = root;
        ThemedChrome.Apply(this);

        try
        {
            _watched = [.. MeterSettings.Load().WatchedFieldBosses];
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // keep the list empty
        }

        Aion2FieldBosses.Changed += OnChanged;
        _timer.Tick += (_, _) => Tick();
        Closed += (_, _) =>
        {
            _timer.Stop();
            Aion2FieldBosses.Changed -= OnChanged;
        };
        Loaded += (_, _) =>
        {
            Tick();
            _timer.Start();
        };
    }

    /// <summary>Raised after a star was toggled and the setting saved; MainWindow reloads its watched list.</summary>
    public event Action? WatchedChanged;

    private void OnChanged() => Dispatcher.BeginInvoke(new Action(Tick));

    private void Tick()
    {
        DateTime now = DateTime.Now;
        string language = LocalizationManager.Instance.Language;

        var maps = Aion2FieldBosses.Maps.Select(m => (Map: m, Known: m.Bosses.Any(b => Aion2FieldBosses.StatusOf(b, now).Phase != BossPhase.Unknown)))
            .OrderByDescending(x => x.Known).ToList();
        string signature = language + "|" + string.Join(";", maps.Select(x => x.Map.Key + (x.Known ? "+" : "-")))
            + "|" + string.Join(",", _watched.Order());
        if (signature != _signature)
        {
            _signature = signature;
            double offset = _scroll.VerticalOffset;
            Build(maps, language);
            _scroll.ScrollToVerticalOffset(offset);
        }

        foreach (var update in _updaters)
        {
            update(now);
        }
    }

    private void Build(List<(FieldBossMap Map, bool Known)> maps, string language)
    {
        var loc = LocalizationManager.Instance;
        Title = loc["FieldBosses.Title"];
        _list.Children.Clear();
        _updaters.Clear();

        _list.Children.Add(new TextBlock
        {
            Text = loc["FieldBosses.Help"], TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12, Margin = new Thickness(18, 14, 14, 8),
        });

        foreach (var (map, known) in maps)
        {
            _list.Children.Add(BuildMap(map, known, language));
        }

        _list.Children.Add(new TextBlock
        {
            Text = loc["FieldBosses.Credit"], Foreground = Muted, FontSize = 11, Margin = new Thickness(18, 6, 14, 14),
        });
    }

    private UIElement BuildMap(FieldBossMap map, bool known, string language)
    {
        var loc = LocalizationManager.Instance;
        var bosses = map.Bosses.OrderByDescending(b => _watched.Contains(b.Npc)).ToList(); // stable: the map's own order otherwise

        var body = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var seenText = new TextBlock { FontSize = 11, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        _updaters.Add(now =>
        {
            DateTime? seen = map.Bosses.Select(b => Aion2FieldBosses.StatusOf(b, now).SeenAt).Where(s => s is not null).DefaultIfEmpty(null).Max();
            seenText.Text = seen is { } at ? string.Format(loc["FieldBosses.Seen"], at) : "";
        });

        foreach (FieldBoss boss in bosses)
        {
            body.Children.Add(BuildBoss(map, boss, language));
        }

        bool open = known || _opened.Contains(map.Key);
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        var arrow = new TextBlock { Text = open ? "▾" : "▸", Foreground = Muted, Width = 16, VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel { Background = Brushes.Transparent, Cursor = known ? Cursors.Arrow : Cursors.Hand };
        if (!known)
        {
            DockPanel.SetDock(arrow, Dock.Left);
            head.Children.Add(arrow);
        }

        DockPanel.SetDock(seenText, Dock.Right);
        head.Children.Add(seenText);
        head.Children.Add(new TextBlock
        {
            Text = map.NameIn(language), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Foreground = known ? Brushes.White : Quiet, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!known)
        {
            head.MouseLeftButtonUp += (_, _) =>
            {
                bool nowOpen = body.Visibility != Visibility.Visible;
                body.Visibility = nowOpen ? Visibility.Visible : Visibility.Collapsed;
                arrow.Text = nowOpen ? "▾" : "▸";
                if (nowOpen)
                {
                    _opened.Add(map.Key);
                }
                else
                {
                    _opened.Remove(map.Key);
                }
            };
        }

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        return new Border
        {
            Background = CardBack, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 7), Margin = new Thickness(12, 4, 8, 4), Child = stack,
        };
    }

    private UIElement BuildBoss(FieldBossMap map, FieldBoss boss, string language)
    {
        var loc = LocalizationManager.Instance;
        bool watched = _watched.Contains(boss.Npc);
        var star = new TextBlock
        {
            Text = watched ? "★" : "☆", FontSize = 17, Foreground = watched ? VioletText : Muted, Cursor = Cursors.Hand, Background = Brushes.Transparent,
            Width = 26, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = loc["FieldBosses.Watch"],
        };
        star.MouseLeftButtonUp += (_, e) =>
        {
            Toggle(boss.Npc);
            e.Handled = true;
        };

        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        _updaters.Add(now =>
        {
            BossStatus s = Aion2FieldBosses.StatusOf(boss, now);
            TimeSpan? left = s.Remaining(now);
            if (s.Phase == BossPhase.Now)
            {
                status.Text = loc["FieldBosses.Here"];
                status.Foreground = Green;
                status.FontWeight = FontWeights.SemiBold;
            }
            else if (s.Phase is BossPhase.Respawn or BossPhase.Fixed && s.At is { } at && left is { } l)
            {
                status.Text = string.Format(loc["FieldBosses.Back"], GameEventTimers.Countdown(l), at);
                status.Foreground = l <= TimeSpan.FromMinutes(10) ? VioletText : Quiet;
                status.FontWeight = l <= TimeSpan.FromMinutes(10) ? FontWeights.SemiBold : FontWeights.Normal;
            }
            else
            {
                status.Text = loc["FieldBosses.Unknown"];
                status.Foreground = Muted;
                status.FontWeight = FontWeights.Normal;
            }
        });

        var row = new DockPanel { Height = 30 };
        DockPanel.SetDock(star, Dock.Left);
        DockPanel.SetDock(status, Dock.Right);
        row.Children.Add(star);
        row.Children.Add(status);
        row.Children.Add(new TextBlock
        {
            Text = Aion2FieldBosses.DisplayName(map, boss, language), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 10, 0), FontWeight = watched ? FontWeights.SemiBold : FontWeights.Normal,
        });
        return row;
    }

    private void Toggle(int npc)
    {
        MeterSettings settings = MeterSettings.Load();
        var list = settings.WatchedFieldBosses.ToList();
        if (!list.Remove(npc))
        {
            list.Add(npc);
        }

        settings.WatchedFieldBosses = list;
        settings.Save();
        _watched = [.. list];
        WatchedChanged?.Invoke();
        Tick();
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b) => Frozen(0xFF, r, g, b);

    private static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
