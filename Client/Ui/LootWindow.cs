using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// What the player picked up, by session (left) and by map (right). Double-click on an item gives it a name
/// of the player's own. Code-built, in the look of FieldBossWindow; rebuilt when the loot changes.
/// </summary>
public sealed class LootWindow : Window
{
    private static readonly Brush Muted = Frozen(0xFF, 0x8F, 0xA3, 0xB3);
    private static readonly Brush Quiet = Frozen(0xFF, 0xB4, 0xC3, 0xCF);
    private static readonly Brush VioletText = Frozen(0xFF, 0xB7, 0xA4, 0xFF);
    private static readonly Brush CardBack = Frozen(0x1A, 0xFF, 0xFF, 0xFF);
    private static readonly Brush CardSelected = Frozen(0x33, 0xB7, 0xA4, 0xFF);

    private readonly StackPanel _sessions = new() { Margin = new Thickness(8, 8, 4, 8) };
    private readonly StackPanel _detail = new();
    private readonly ScrollViewer _detailScroll;
    private DateTime? _selected;
    private bool _editing;

    public LootWindow()
    {
        Title = LocalizationManager.Instance["Loot.Title"];
        Width = 640;
        Height = 520;
        MinWidth = 480;
        MinHeight = 320;
        FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Foreground = Frozen(0xFF, 0xEA, 0xF0, 0xF5);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        System.Windows.Documents.Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

        var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _sessions, Width = 210 };
        _detailScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _detail, Padding = new Thickness(0, 0, 4, 0) };
        var root = new Grid
        {
            Background = new LinearGradientBrush(Color.FromRgb(0x14, 0x1C, 0x24), Color.FromRgb(0x0B, 0x10, 0x16), 90),
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_detailScroll, 1);
        root.Children.Add(leftScroll);
        root.Children.Add(_detailScroll);
        Content = root;
        ThemedChrome.Apply(this);

        Aion2Loot.Changed += OnChanged;
        Closed += (_, _) => Aion2Loot.Changed -= OnChanged;
        Loaded += (_, _) => Build();
    }

    private void OnChanged() => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (!_editing)
        {
            Build();
        }
    }));

    private void Build()
    {
        var loc = LocalizationManager.Instance;
        string language = loc.Language;
        Title = loc["Loot.Title"];
        var sessions = Aion2Loot.Sessions;
        if (_selected is null || sessions.All(s => s.Start != _selected))
        {
            _selected = sessions.Count > 0 ? sessions[0].Start : null;
        }

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.CurrentUICulture;
        }

        _sessions.Children.Clear();
        _detail.Children.Clear();
        double offset = _detailScroll.VerticalOffset;

        if (sessions.Count == 0)
        {
            _detail.Children.Add(new TextBlock
            {
                Text = loc["Loot.Empty"], TextWrapping = TextWrapping.Wrap, Foreground = Muted, TextAlignment = TextAlignment.Center,
                Margin = new Thickness(40, 90, 40, 0), HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }

        foreach (LootSession s in sessions)
        {
            _sessions.Children.Add(BuildCard(s, culture));
        }

        LootSession current = sessions.First(s => s.Start == _selected);
        foreach (LootMap map in current.Maps.OrderByDescending(m => m.Items.Count > 0 ? m.Items.Max(i => i.At) : DateTime.MinValue))
        {
            _detail.Children.Add(BuildMap(map, language));
        }

        _detailScroll.ScrollToVerticalOffset(offset);
    }

    private UIElement BuildCard(LootSession s, CultureInfo culture)
    {
        var loc = LocalizationManager.Instance;
        long total = s.Maps.Sum(m => m.Items.Sum(i => i.Count));
        bool selected = s.Start == _selected;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = s.Start.ToString("ddd d/MM", culture), FontWeight = FontWeights.SemiBold, Foreground = selected ? Brushes.White : Quiet });
        stack.Children.Add(new TextBlock { Text = $"{s.Start:HH:mm} - {s.End:HH:mm}", Foreground = Muted, FontSize = 12 });
        stack.Children.Add(new TextBlock { Text = string.Format(loc["Loot.Items"], total), Foreground = selected ? VioletText : Muted, FontSize = 12 });
        var card = new Border
        {
            Background = selected ? CardSelected : CardBack, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand, Child = stack,
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            _selected = s.Start;
            Build();
        };
        return card;
    }

    private UIElement BuildMap(LootMap map, string language)
    {
        var body = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (LootItem item in map.Items.OrderByDescending(i => i.At))
        {
            body.Children.Add(BuildRow(item, language));
        }

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = Aion2Loot.ZoneName(map.Zone, language), FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        stack.Children.Add(body);
        return new Border
        {
            Background = CardBack, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 7), Margin = new Thickness(8, 8, 8, 0), Child = stack,
        };
    }

    private UIElement BuildRow(LootItem item, string language)
    {
        var loc = LocalizationManager.Instance;
        var row = new Grid { Height = 28, Background = Brushes.Transparent, ToolTip = loc["Loot.RenameTip"] };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

        var name = new TextBlock
        {
            Text = Aion2ItemNames.Name(item.ItemId, language), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var count = new TextBlock { Text = "×" + item.Count, Foreground = VioletText, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, FontWeight = FontWeights.SemiBold };
        var time = new TextBlock { Text = item.At.ToString("HH:mm"), Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        Grid.SetColumn(count, 1);
        Grid.SetColumn(time, 2);
        row.Children.Add(name);
        row.Children.Add(count);
        row.Children.Add(time);

        row.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2 && !_editing)
            {
                e.Handled = true;
                Edit(row, name, item);
            }
        };
        return row;
    }

    private void Edit(Grid row, TextBlock name, LootItem item)
    {
        _editing = true;
        var box = new TextBox
        {
            Text = name.Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2), Padding = new Thickness(4, 1, 4, 1),
            Background = Frozen(0xFF, 0x0B, 0x10, 0x16), Foreground = Brushes.White, CaretBrush = Brushes.White,
            BorderBrush = VioletText, BorderThickness = new Thickness(1),
        };
        name.Visibility = Visibility.Collapsed;
        row.Children.Add(box);
        bool done = false;
        void Finish(bool save)
        {
            if (done)
            {
                return;
            }

            done = true;
            _editing = false;
            if (save && box.Text.Trim() != name.Text)
            {
                Aion2ItemNames.SetUserName(item.ItemId, box.Text.Trim());
            }

            Build();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Finish(true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Finish(false);
            }
        };
        box.LostKeyboardFocus += (_, _) => Finish(false);
        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
    }

    private static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
