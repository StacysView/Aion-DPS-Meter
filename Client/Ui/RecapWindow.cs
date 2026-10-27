using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AionDPS.Aion2;

namespace AionDPS.Ui;

/// <summary>
/// "Recap du jour" (App menu): per character, what is left to do today and this week - Odyle energy, Shugo Festival, Nightmare,
/// Dimensional Invasion, Ascension rite, the daily dungeon, the five mandatory tasks, the weekly Odyle purchase, the time left in the
/// lower Abyss - as the game last reported it (Aion2Tickets), with the next refills and the hand-ticked weekly purchases (ChecklistManual).
/// Content and rules follow SkeeveTV's ChecklistWindow (Aion 2 DPS Meter, MIT licence, https://github.com/SkeeveAN/Aion-DPS-Meter,
/// Client/Ui/ChecklistWindow.cs at d574db2f); the look is this fork's "modern classic" one: theme tokens, cards, purple accent.
/// Local only: reads what the capture already decoded, writes nothing but its own small JSON files.
/// </summary>
public sealed class RecapWindow : Window
{
    /// <summary>Daily contents, in this order (SkeeveTV's overview list minus the weekly Ascension rite): Shugo Festival, Nightmare, Dimensional Invasion.</summary>
    private static readonly int[] DailyTickets = { 1, 4, 8 };

    /// <summary>Weekly contents from the game: the Ascension rite, then the daily dungeon (its counter is weekly: 14 every Wednesday).</summary>
    private static readonly int[] WeeklyTickets = { 80000001, 101 };

    /// <summary>The access time shown: the lower Abyss.</summary>
    private const int AbyssTimeTicket = 3;

    /// <summary>The Odyle energy charged from stones (the second bar of the game's tooltip) has this cap.</summary>
    private const int OdyleChargedMax = 2000;

    private readonly StackPanel _characters = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _body = new() { Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _name = new();
    private readonly TextBlock _sub = new();
    private readonly Border _pill = new();
    private readonly ScrollViewer _bodyScroll;
    private readonly Grid _root = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? _selected;
    private bool _premium = true;
    private CultureInfo _culture = CultureInfo.CurrentCulture;

    public RecapWindow()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        Title = loc["Recap.Title"];
        Width = 860;
        Height = Math.Min(860, SystemParameters.WorkArea.Height - 40);
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(ForegroundProperty, "Brush.Body");
        SetResourceReference(FontSizeProperty, "FontSize.Base");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        System.Windows.Documents.Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);
        Resources[typeof(Button)] = FlatButtonStyle();
        Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = ThinScrollBarStyle();

        var root = _root;
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: the characters seen, the one logged in last first.
        var side = new Border { Background = new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)), BorderThickness = new Thickness(0, 0, 1, 0) };
        side.SetResourceReference(Border.BorderBrushProperty, "Brush.SideLine");
        var sideDock = new DockPanel();
        TextBlock sideHead = Text(loc["Recap.Characters"], "Brush.Dim", 11.5, bold: true);
        sideHead.Margin = new Thickness(22, 16, 0, 8);
        DockPanel.SetDock(sideHead, Dock.Top);
        sideDock.Children.Add(sideHead);
        sideDock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _characters });
        side.Child = sideDock;
        root.Children.Add(side);

        // Right: the chosen character.
        var main = new DockPanel { Margin = new Thickness(24, 16, 14, 12) };
        Grid.SetColumn(main, 1);
        var header = new DockPanel { Margin = new Thickness(2, 0, 10, 14) };
        DockPanel.SetDock(header, Dock.Top);
        _pill.CornerRadius = new CornerRadius(11);
        _pill.Padding = new Thickness(11, 3, 11, 4);
        _pill.VerticalAlignment = VerticalAlignment.Top;
        _pill.Margin = new Thickness(0, 4, 0, 0);
        DockPanel.SetDock(_pill, Dock.Right);
        header.Children.Add(_pill);
        var titles = new StackPanel();
        _name.SetResourceReference(TextBlock.FontSizeProperty, ThemeManager.FontKey(20));
        _name.FontWeight = FontWeights.SemiBold;
        _name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Heading");
        _name.TextTrimming = TextTrimming.CharacterEllipsis;
        _sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
        _sub.Margin = new Thickness(0, 3, 0, 0);
        _sub.TextWrapping = TextWrapping.Wrap;
        titles.Children.Add(_name);
        titles.Children.Add(_sub);
        header.Children.Add(titles);
        main.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(2, 8, 10, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        TextBlock note = Text(loc["Recap.Note"], "Brush.Muted", 11);
        note.TextWrapping = TextWrapping.Wrap;
        note.FontStyle = FontStyles.Italic;
        TextBlock credit = Text(loc["Recap.Credit"], "Brush.Faint", 10.5);
        credit.TextWrapping = TextWrapping.Wrap;
        credit.Margin = new Thickness(0, 4, 0, 0);
        footer.Children.Add(note);
        footer.Children.Add(credit);
        main.Children.Add(footer);

        _bodyScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body };
        main.Children.Add(_bodyScroll);
        root.Children.Add(main);
        Content = root;
        ThemedChrome.Apply(this);
        Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = ThinScrollBarStyle(); // ThemedChrome may copy the main window's one over it

        Aion2Tickets.Changed += OnDataChanged;
        ChecklistManual.Changed += OnDataChanged;
        _tick.Tick += (_, _) => Render();
        _tick.Start();
        Closed += (_, _) =>
        {
            _tick.Stop();
            Aion2Tickets.Changed -= OnDataChanged;
            ChecklistManual.Changed -= OnDataChanged;
        };
        Refresh();
    }

    /// <summary>The character shown (a test or the preview harness picks one); null = the one logged in, else the latest.</summary>
    public string? SelectedCharacter
    {
        get => _selected;
        set
        {
            _selected = value;
            Refresh();
        }
    }

    private void OnDataChanged() => Dispatcher.BeginInvoke(new Action(Refresh));

    private void Refresh()
    {
        IReadOnlyList<TicketCharacter> all = Aion2Tickets.Snapshot();
        string? current = Aion2Tickets.CurrentName;
        string? wanted = _selected is not null && all.Any(c => c.Name == _selected) ? _selected
            : current is not null && all.Any(c => c.Name == current) ? current
            : all.FirstOrDefault()?.Name;
        _selected = wanted;
        Render();
    }

    private void Render()
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string language = loc.Language;
        Title = loc["Recap.Title"];
        try
        {
            _culture = CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            _culture = CultureInfo.CurrentCulture;
        }

        // the theme's two surface colours, lighter at the top (read again on every render: the theme can change while the window is open)
        Color surfaceTop = TryFindResource("Color.SurfaceTop") is Color t ? t : Color.FromRgb(0x14, 0x1C, 0x24);
        Color surfaceBottom = TryFindResource("Color.SurfaceBottom") is Color b ? b : Color.FromRgb(0x0B, 0x10, 0x16);
        _root.Background = new LinearGradientBrush(surfaceTop, surfaceBottom, 90);

        IReadOnlyList<TicketCharacter> all = Aion2Tickets.Snapshot();
        DateTime now = DateTime.Now;
        RenderCharacters(all);
        double offset = _bodyScroll.VerticalOffset;
        _body.Children.Clear();
        TicketCharacter? character = all.FirstOrDefault(c => c.Name == _selected);
        if (character is null)
        {
            _name.Text = loc["Recap.Title"];
            _sub.Text = "";
            _pill.Visibility = Visibility.Collapsed;
            TextBlock empty = Text(loc["Recap.Empty"], "Brush.Muted", 13);
            empty.TextWrapping = TextWrapping.Wrap;
            empty.Margin = new Thickness(4, 30, 30, 0);
            _body.Children.Add(empty);
            return;
        }

        _premium = Aion2Tickets.HasPremium(character, DateTime.UtcNow) != false;
        DateTime seenAt = character.SavedAt.ToLocalTime();
        bool online = Aion2Tickets.CurrentName == character.Name;
        _name.Text = character.Name;
        var sub = new List<string>();
        if (character.Level > 0)
        {
            sub.Add(string.Format(_culture, loc["Recap.Level"], character.Level));
        }

        if (character.ServerId > 0)
        {
            sub.Add(Aion2.Protocol.Aion2Servers.NameOf(character.ServerId));
        }

        sub.Add(string.Format(_culture, loc["Recap.ReadAt"], When(seenAt, now)));
        _sub.Text = string.Join("  ·  ", sub);

        if (Aion2Tickets.CurrentName is null)
        {
            // the counters come with the login: a meter started after the login has not read them yet
            _body.Children.Add(Hint(loc["Recap.NeedLogin"]));
        }

        int open = 0;
        _body.Children.Add(OdyleCard(character, language));
        _body.Children.Add(TodayCard(character, seenAt, now, language, ref open));
        _body.Children.Add(WeekCard(character, seenAt, now, language, ref open));
        if (TimeCard(character, language) is { } times)
        {
            _body.Children.Add(times);
        }

        _pill.Visibility = Visibility.Visible;
        _pill.SetResourceReference(Border.BackgroundProperty, open > 0 ? "Brush.BrandSelect" : "Brush.Button");
        TextBlock pillText = Text(open > 0 ? string.Format(_culture, loc["Recap.Open"], open) : loc["Recap.AllDone"], open > 0 ? "Brush.BrandText" : "Brush.Label", 11.5, bold: true);
        _pill.Child = pillText;
        _pill.ToolTip = online ? loc["Recap.Online"] : null;
        _bodyScroll.ScrollToVerticalOffset(offset);
    }

    // ---- the character list ----

    private void RenderCharacters(IReadOnlyList<TicketCharacter> all)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        _characters.Children.Clear();
        foreach (TicketCharacter c in all)
        {
            string name = c.Name;
            bool chosen = name == _selected;
            bool online = Aion2Tickets.CurrentName == name;
            var lines = new StackPanel();
            var top = new DockPanel();
            if (online)
            {
                var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = loc["Recap.Online"] };
                dot.SetResourceReference(Shape.FillProperty, "Brush.BrandLight");
                DockPanel.SetDock(dot, Dock.Right);
                top.Children.Add(dot);
            }

            // The class: its icon before the name, the name in the class colour, the class name first on the line below
            // (Stacy, 2026-10-17: alts with close names were hard to tell apart).
            string? className = Aion2.Protocol.Aion2SkillNames.ClassFromCode(c.ClassCode);
            if (className is not null && new ClassIconConverter().Convert(className, typeof(ImageSource), null, _culture) is ImageSource icon)
            {
                var image = new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(image, Dock.Left);
                top.Children.Add(image);
            }

            TextBlock title = Text(name, chosen ? "Brush.Heading" : "Brush.BodySoft", 13, bold: true);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            if (className is not null)
            {
                title.Foreground = ClassColors.For(className);
            }

            top.Children.Add(title);
            lines.Children.Add(top);
            var parts = new List<string>();
            if (className is not null)
            {
                parts.Add(AionDPS.Data.ClassCatalog.DisplayName(className, loc.Language));
            }

            if (c.Level > 0)
            {
                parts.Add(string.Format(_culture, loc["Recap.Level"], c.Level));
            }

            if (c.ServerId > 0)
            {
                parts.Add(Aion2.Protocol.Aion2Servers.NameOf(c.ServerId).Replace("Europe - ", "", StringComparison.Ordinal));
            }

            TextBlock info = Text(string.Join(" · ", parts), "Brush.Muted", 11.5);
            info.Margin = new Thickness(0, 3, 0, 0);
            info.TextTrimming = TextTrimming.CharacterEllipsis;
            lines.Children.Add(info);
            TextBlock seen = Text(When(c.SavedAt.ToLocalTime(), DateTime.Now), "Brush.Dim", 11);
            seen.Margin = new Thickness(0, 1, 0, 0);
            lines.Children.Add(seen);

            var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(13, 9, 12, 9), Child = lines, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            if (chosen)
            {
                card.SetResourceReference(Border.BackgroundProperty, "Brush.BrandNav");
            }
            else
            {
                card.MouseEnter += (_, _) => card.Background = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
                card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
            }

            var holder = new Grid { Margin = new Thickness(10, 0, 10, 4) };
            holder.Children.Add(card);
            if (chosen)
            {
                var mark = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 10) };
                mark.SetResourceReference(Border.BackgroundProperty, "Brush.Brand");
                holder.Children.Add(mark);
            }

            holder.MouseLeftButtonUp += (_, _) =>
            {
                _selected = name;
                Render();
            };
            _characters.Children.Add(holder);
        }
    }

    // ---- the cards ----

    /// <summary>The Odyle energy as the game's tooltip shows it: the basic energy (cap 560, 840 with the membership) and the charged one (cap 2000).</summary>
    private UIElement OdyleCard(TicketCharacter character, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        TicketDef? def = Aion2Tickets.Find(Aion2Tickets.OdyleId);
        bool locked = Aion2Tickets.IsLocked(character, Aion2Tickets.OdyleId);
        character.Tickets.TryGetValue(Aion2Tickets.OdyleId, out TicketValue value);
        if (locked)
        {
            value = default;
        }

        var stack = new StackPanel();
        stack.Children.Add(CardTitle(def?.NameIn(language) ?? loc["Recap.Odyle"], locked ? string.Format(_culture, loc["Recap.Locked"], Aion2Tickets.UnlockLevel(Aion2Tickets.OdyleId)) : null));
        int baseCap = def is null ? 840 : _premium ? Math.Max(def.Max, def.SubscribeMax) : def.Max;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        UIElement basic = Meter(loc["Recap.OdyleBase"], value.Count, Math.Max(baseCap, value.Count), locked);
        UIElement charged = Meter(loc["Recap.OdyleCharged"], value.Bonus, Math.Max(OdyleChargedMax, value.Bonus), locked);
        Grid.SetColumn(charged, 2);
        grid.Children.Add(basic);
        grid.Children.Add(charged);
        stack.Children.Add(grid);
        return Card(stack);
    }

    private UIElement Meter(string label, int value, int cap, bool locked)
    {
        var stack = new StackPanel();
        var line = new DockPanel();
        var numbers = new TextBlock { VerticalAlignment = VerticalAlignment.Bottom };
        var big = new System.Windows.Documents.Run(value.ToString("N0", _culture)) { FontWeight = FontWeights.SemiBold };
        big.SetResourceReference(System.Windows.Documents.TextElement.FontSizeProperty, ThemeManager.FontKey(19));
        big.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, locked ? "Brush.Faint" : value > 0 ? "Brush.Heading" : "Brush.Muted");
        var rest = new System.Windows.Documents.Run(" / " + cap.ToString("N0", _culture));
        rest.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Muted");
        numbers.Inlines.Add(big);
        numbers.Inlines.Add(rest);
        DockPanel.SetDock(numbers, Dock.Right);
        line.Children.Add(numbers);
        TextBlock name = Text(label, "Brush.Label", 12);
        name.VerticalAlignment = VerticalAlignment.Bottom;
        name.Margin = new Thickness(0, 0, 0, 3);
        line.Children.Add(name);
        stack.Children.Add(line);
        stack.Children.Add(Bar(locked ? 0 : value, cap));
        return stack;
    }

    /// <summary>Today: the mandatory tasks of the home region (from the game, handed in of five), then the daily contents with what is left.</summary>
    private UIElement TodayCard(TicketCharacter character, DateTime seenAt, DateTime now, string language, ref int open)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        DateTime nextDaily = ChecklistManual.LastDailyReset(now).AddDays(1);
        stack.Children.Add(CardTitle(loc["Recap.Today"], string.Format(_culture, loc["Recap.ResetAt"], When(nextDaily, now))));
        Grid grid = RowsGrid();

        // the five mandatory tasks: the count read last, or 0 when a daily reset came since that reading
        string region = loc[character.ServerId / 1000 == 2 ? "Recap.RegionAsmodian" : "Recap.RegionElyos"];
        bool known = character.Tickets.TryGetValue(Aion2Tickets.MissionsId, out TicketValue missions);
        bool stale = known && Aion2Tickets.MissionsStale(missions, now);
        int done = stale ? 0 : missions.Count;
        bool missionsOpen = done < Aion2Tickets.MissionsPerDay;
        open += missionsOpen ? 1 : 0;
        AddRow(grid, string.Format(_culture, loc["Recap.DailyTasks"], region), done, Aion2Tickets.MissionsPerDay, estimated: stale, countsUp: true,
            note: known ? null : loc["Recap.NotSeen"]);

        foreach (int id in DailyTickets)
        {
            if (Aion2Tickets.Find(id) is { } def)
            {
                AddTicketRow(grid, def, character, seenAt, now, language, ref open);
            }
        }

        stack.Children.Add(grid);
        return Card(stack);
    }

    /// <summary>This week: the Ascension rite and the daily dungeon (from the game), the Odyle energy bought from the Brisenwind trade guild
    /// (from the game, 0x8E56), then what the meter cannot see and the player ticks by hand.</summary>
    private UIElement WeekCard(TicketCharacter character, DateTime seenAt, DateTime now, string language, ref int open)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        var stack = new StackPanel();
        DateTime nextWeekly = ChecklistManual.LastWeeklyReset(now).AddDays(7);
        stack.Children.Add(CardTitle(loc["Recap.Week"], string.Format(_culture, loc["Recap.ResetAt"], When(nextWeekly, now))));
        Grid grid = RowsGrid();
        foreach (int id in WeeklyTickets)
        {
            if (Aion2Tickets.Find(id) is { } def)
            {
                AddTicketRow(grid, def, character, seenAt, now, language, ref open);
            }
        }

        // the purchase counter of the trade guild: an older reset time means the week is over
        bool shopKnown = character.Tickets.TryGetValue(Aion2Tickets.ShopOdyleId, out TicketValue shop);
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int bought = shop.TimeMs > nowMs ? Math.Min(shop.Count, Aion2Tickets.ShopOdyleMax) : 0;
        open += bought < Aion2Tickets.ShopOdyleMax ? 1 : 0;
        AddRow(grid, loc["Recap.ShopOdyle"], bought, Aion2Tickets.ShopOdyleMax, estimated: false, countsUp: true, note: shopKnown ? null : loc["Recap.NotSeen"]);

        // hand-ticked: the scroll purchases (the shop limit is per server: shared by the characters) and the two Odyle conversions (per character)
        ChecklistManual.State shared = ChecklistManual.Get(ChecklistManual.SharedKey, now);
        ChecklistManual.State own = ChecklistManual.Get(character.Name, now);
        AddSeparator(grid, loc["Recap.ByHand"]);
        AddManualRow(grid, loc["Recap.ScrollVerteron"] /* "(hebdomadaire)" rather than the scroll's own name (Stacy, 2026-10-17) */, shared.Verteron, ChecklistManual.VerteronMax, loc["Recap.HandShared"],
            d => ChecklistManual.Step(ChecklistManual.SharedKey, ChecklistManual.Counter.Verteron, d, DateTime.Now), ref open);
        AddManualRow(grid, loc["Recap.ScrollAbyss"], shared.Abyss, ChecklistManual.AbyssMax, loc["Recap.HandShared"],
            d => ChecklistManual.Step(ChecklistManual.SharedKey, ChecklistManual.Counter.Abyss, d, DateTime.Now), ref open);
        AddManualRow(grid, loc["Recap.ConvertBig"], own.ConvertBig, ChecklistManual.ConvertBigMax, loc["Recap.HandOwn"],
            d => ChecklistManual.Step(character.Name, ChecklistManual.Counter.ConvertBig, d, DateTime.Now), ref open);
        AddManualRow(grid, loc["Recap.ConvertSmall"], own.ConvertSmall, ChecklistManual.ConvertSmallMax, loc["Recap.HandOwn"],
            d => ChecklistManual.Step(character.Name, ChecklistManual.Counter.ConvertSmall, d, DateTime.Now), ref open);
        stack.Children.Add(grid);
        return Card(stack);
    }

    /// <summary>The time left in the lower Abyss (as read) and of the Quai premium membership.</summary>
    private UIElement? TimeCard(TicketCharacter character, string language)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        Grid grid = RowsGrid();
        bool any = false;
        if (Aion2Tickets.Find(AbyssTimeTicket) is { } abyss && (character.Tickets.ContainsKey(abyss.Id) || Aion2Tickets.IsLocked(character, abyss.Id)))
        {
            bool locked = Aion2Tickets.IsLocked(character, abyss.Id);
            TimeSpan left = locked ? TimeSpan.Zero : TimeSpan.FromMilliseconds(character.Tickets[abyss.Id].TimeMs);
            AddValueRow(grid, loc["Recap.AbyssTime"], locked ? string.Format(_culture, loc["Recap.Locked"], Aion2Tickets.UnlockLevel(abyss.Id)) : $"{(int)left.TotalHours}:{left.Minutes:00}", left > TimeSpan.Zero);
            any = true;
        }

        if (character.Tickets.TryGetValue(Aion2Tickets.PremiumId, out TicketValue premium))
        {
            TimeSpan left = premium.TimeMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(premium.TimeMs) - DateTimeOffset.UtcNow : TimeSpan.Zero;
            AddValueRow(grid, loc["Recap.Premium"], left > TimeSpan.Zero ? string.Format(_culture, loc["Recap.DaysHours"], (int)left.TotalDays, left.Hours) : loc["Recap.None"], left > TimeSpan.Zero);
            any = true;
        }

        if (!any)
        {
            return null;
        }

        var stack = new StackPanel();
        stack.Children.Add(CardTitle(loc["Recap.Time"], null));
        stack.Children.Add(grid);
        return Card(stack);
    }

    // ---- rows ----

    /// <summary>Columns: state mark | name | count | next refill | (+/- for the hand-ticked ones).</summary>
    private static Grid RowsGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        return grid;
    }

    /// <summary>A content from the game: what is left (a guess from the refills since the reading gets "~"), its next refill; grey below the level it needs.</summary>
    private void AddTicketRow(Grid grid, TicketDef def, TicketCharacter character, DateTime seenAt, DateTime now, string language, ref int open)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        bool locked = Aion2Tickets.IsLocked(character, def.Id);
        bool known = character.Tickets.TryGetValue(def.Id, out TicketValue value);
        int cap = _premium && def.SubscribeMax > def.Max ? def.SubscribeMax : def.Max;
        int row = NewRow(grid);
        if (locked)
        {
            Place(grid, Mark(MarkKind.Locked), row, 0);
            Place(grid, Text(def.ShortIn(language), "Brush.Faint", 13), row, 1);
            TextBlock level = Text(string.Format(_culture, loc["Recap.Locked"], Aion2Tickets.UnlockLevel(def.Id)), "Brush.Faint", 12);
            level.HorizontalAlignment = HorizontalAlignment.Right;
            Place(grid, level, row, 2);
            return;
        }

        int count = known ? Aion2Tickets.Expected(def, value.Count, seenAt, now, _premium) : 0;
        bool estimated = known && count != value.Count;
        bool left = count > 0;
        open += left ? 1 : 0;
        Place(grid, Mark(left ? MarkKind.Open : MarkKind.Done), row, 0);
        TextBlock name = Text(def.ShortIn(language), left ? "Brush.Body" : "Brush.Dim", 13);
        name.ToolTip = def.NameIn(language);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        Place(grid, name, row, 1);
        Place(grid, Count(known ? count : null, Math.Max(cap, count), estimated, left, value.Bonus), row, 2);
        if (Aion2Tickets.NextReset(def, now) is { } next)
        {
            int amount = def.DailyCount > 0 ? def.DailyCount : def.WeeklyCount;
            TextBlock refill = Text($"+{amount}  ·  {When(next, now)}", "Brush.Dim", 11.5);
            refill.HorizontalAlignment = HorizontalAlignment.Right;
            refill.ToolTip = string.Format(_culture, loc["Recap.NextRefill"], next.ToString("f", _culture));
            Place(grid, refill, row, 3);
        }
    }

    /// <summary>A counter that counts up to its cap (handed in, bought).</summary>
    private void AddRow(Grid grid, string label, int count, int cap, bool estimated, bool countsUp, string? note = null)
    {
        int row = NewRow(grid);
        bool left = countsUp ? count < cap : count > 0;
        Place(grid, Mark(left ? MarkKind.Open : MarkKind.Done), row, 0);
        TextBlock name = Text(label, left ? "Brush.Body" : "Brush.Dim", 13);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.ToolTip = label;
        Place(grid, name, row, 1);
        Place(grid, Count(count, cap, estimated, left), row, 2);
        if (note is not null)
        {
            TextBlock hint = Text(note, "Brush.Faint", 11);
            hint.HorizontalAlignment = HorizontalAlignment.Right;
            Place(grid, hint, row, 3);
        }
    }

    private void AddManualRow(Grid grid, string label, int count, int cap, string tip, Action<int> step, ref int open)
    {
        int row = NewRow(grid);
        bool left = count < cap;
        open += left ? 1 : 0;
        Place(grid, Mark(left ? MarkKind.Hand : MarkKind.Done), row, 0);
        TextBlock name = Text(label, left ? "Brush.Body" : "Brush.Dim", 13);
        name.ToolTip = label + Environment.NewLine + tip;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        Place(grid, name, row, 1);
        Place(grid, Count(count, cap, false, left), row, 2);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(StepButton("−", () => step(-1), count > 0));
        buttons.Children.Add(StepButton("+", () => step(+1), count < cap));
        Place(grid, buttons, row, 4);
    }

    private void AddValueRow(Grid grid, string label, string value, bool active)
    {
        int row = NewRow(grid);
        Place(grid, Mark(active ? MarkKind.Time : MarkKind.Done), row, 0);
        Place(grid, Text(label, active ? "Brush.Body" : "Brush.Dim", 13), row, 1);
        TextBlock number = Text(value, active ? "Brush.Heading" : "Brush.Muted", 13, bold: true);
        number.HorizontalAlignment = HorizontalAlignment.Right;
        Place(grid, number, row, 2);
    }

    private static void AddSeparator(Grid grid, string label)
    {
        int row = NewRow(grid);
        var line = new DockPanel { Margin = new Thickness(0, 8, 0, 2) };
        TextBlock text = Text(label, "Brush.Dim", 11, bold: true);
        text.Margin = new Thickness(0, 0, 10, 0);
        line.Children.Add(text);
        var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center };
        rule.SetResourceReference(Border.BackgroundProperty, "Brush.Divider");
        line.Children.Add(rule);
        Grid.SetColumnSpan(line, 5);
        Place(grid, line, row, 0);
        Grid.SetColumnSpan(line, 5);
    }

    private static int NewRow(Grid grid)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        return grid.RowDefinitions.Count - 1;
    }

    private static void Place(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        if (element is FrameworkElement fe && fe.VerticalAlignment == VerticalAlignment.Stretch)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
        }

        grid.Children.Add(element);
    }

    /// <summary>"12 / 21": the count bright while something is left, the cap muted; "~" for a guess, "(+n)" for extra entries; "-" when never read.</summary>
    private TextBlock Count(int? count, int cap, bool estimated, bool left, int bonus = 0)
    {
        var block = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.FontSizeProperty, ThemeManager.FontKey(13));
        var now = new System.Windows.Documents.Run((estimated ? "~" : "") + (count?.ToString(_culture) ?? "–")) { FontWeight = FontWeights.SemiBold };
        now.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, left ? "Brush.BrandText" : "Brush.Muted");
        block.Inlines.Add(now);
        if (bonus > 0)
        {
            var extra = new System.Windows.Documents.Run($" (+{bonus.ToString(_culture)})");
            extra.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.BrandLight");
            block.Inlines.Add(extra);
        }

        var rest = new System.Windows.Documents.Run(" / " + cap.ToString(_culture));
        rest.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Brush.Dim");
        block.Inlines.Add(rest);
        return block;
    }

    private enum MarkKind { Open, Done, Hand, Locked, Time }

    /// <summary>The state at the start of a row: a purple dot (something left), a check (done), a ring (ticked by hand), a lock (level too low).</summary>
    private static FrameworkElement Mark(MarkKind kind)
    {
        var holder = new Grid { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        switch (kind)
        {
            case MarkKind.Open:
                var dot = new Ellipse { Width = 8, Height = 8 };
                dot.SetResourceReference(Shape.FillProperty, "Brush.Brand");
                holder.Children.Add(dot);
                break;
            case MarkKind.Hand:
                var ring = new Ellipse { Width = 9, Height = 9, StrokeThickness = 1.5 };
                ring.SetResourceReference(Shape.StrokeProperty, "Brush.BrandLight");
                holder.Children.Add(ring);
                break;
            case MarkKind.Time:
                var clock = new Path { Data = Geometry.Parse("M7,1 A6,6 0 1 1 6.99,1 M7,4 L7,7.4 L9.4,8.6"), StrokeThickness = 1.3, Width = 14, Height = 14, Stretch = Stretch.Uniform };
                clock.SetResourceReference(Shape.StrokeProperty, "Brush.BrandLight");
                holder.Children.Add(clock);
                break;
            case MarkKind.Locked:
                var lockShape = new Path { Data = Geometry.Parse("M3,7 L11,7 L11,13 L3,13 Z M5,7 L5,4.5 A2,2 0 0 1 9,4.5 L9,7"), StrokeThickness = 1.2, Width = 11, Height = 12, Stretch = Stretch.Uniform };
                lockShape.SetResourceReference(Shape.StrokeProperty, "Brush.Faint");
                holder.Children.Add(lockShape);
                break;
            default:
                var tick = new Path { Data = Geometry.Parse("M1,7 L5,11 L13,2"), StrokeThickness = 1.8, Width = 12, Height = 10, Stretch = Stretch.Uniform, StrokeEndLineCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round };
                tick.SetResourceReference(Shape.StrokeProperty, "Brush.Muted");
                holder.Children.Add(tick);
                break;
        }

        return holder;
    }

    private static UIElement StepButton(string glyph, Action action, bool enabled)
    {
        var text = Text(glyph, enabled ? "Brush.Body" : "Brush.Faint", 13, bold: true);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.Margin = new Thickness(0, -2, 0, 0);
        var button = new Border { Width = 24, Height = 22, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(4, 0, 0, 0), Child = text };
        button.SetResourceReference(Border.BackgroundProperty, "Brush.Button");
        button.SetResourceReference(Border.BorderBrushProperty, "Brush.ButtonEdge");
        if (enabled)
        {
            button.Cursor = Cursors.Hand;
            button.MouseEnter += (_, _) => button.SetResourceReference(Border.BorderBrushProperty, "Brush.BrandEdge");
            button.MouseLeave += (_, _) => button.SetResourceReference(Border.BorderBrushProperty, "Brush.ButtonEdge");
            button.MouseLeftButtonUp += (_, e) =>
            {
                action();
                e.Handled = true;
            };
        }
        else
        {
            button.Opacity = 0.5;
        }

        return button;
    }

    // ---- small parts ----

    private static UIElement CardTitle(string title, string? aside)
    {
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (aside is not null)
        {
            TextBlock right = Text(aside, "Brush.Dim", 11.5);
            right.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(right, Dock.Right);
            line.Children.Add(right);
        }

        line.Children.Add(Text(title, "Brush.Heading", 14, bold: true));
        return line;
    }

    private static Border Card(UIElement child)
    {
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 14, 10), Margin = new Thickness(0, 0, 0, 10), Child = child };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Card");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Frame");
        return border;
    }

    private static UIElement Hint(string text)
    {
        TextBlock block = Text(text, "Brush.BrandText", 12);
        block.TextWrapping = TextWrapping.Wrap;
        var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 0, 0, 10), Child = block };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.BrandNav");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.BrandEdge");
        return border;
    }

    private static UIElement Bar(int value, int cap)
    {
        var track = new Grid { Height = 6, Margin = new Thickness(0, 6, 0, 2) };
        var back = new Border { CornerRadius = new CornerRadius(3) };
        back.SetResourceReference(Border.BackgroundProperty, "Brush.Field");
        var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, "Brush.Brand");
        double share = cap <= 0 ? 0 : Math.Clamp(value / (double)cap, 0, 1);
        track.SizeChanged += (_, _) => fill.Width = track.ActualWidth * share;
        track.Children.Add(back);
        track.Children.Add(fill);
        return track;
    }

    private static TextBlock Text(string text, string brush, double size, bool bold = false)
    {
        var block = new TextBlock { Text = text, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        block.SetResourceReference(TextBlock.FontSizeProperty, ThemeManager.FontKey(size));
        return block;
    }

    /// <summary>A moment as "14:32" (today), "tomorrow 09:00", or "Wed 09:00" / "12/10 09:00" further away.</summary>
    private string When(DateTime at, DateTime now)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string time = at.ToString("t", _culture);
        int days = (at.Date - now.Date).Days;
        return days switch
        {
            0 => time,
            1 => string.Format(_culture, loc["Recap.Tomorrow"], time),
            -1 => string.Format(_culture, loc["Recap.Yesterday"], time),
            > 1 and < 7 => at.ToString("ddd", _culture) + " " + time,
            _ => at.ToString("d", _culture) + " " + time,
        };
    }


    /// <summary>A thin scroll bar, the thumb only, in the theme's colour (as in the history window).</summary>
    private static Style ThinScrollBarStyle() => (Style)System.Windows.Markup.XamlReader.Parse(
        """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ScrollBar">
          <Setter Property="Width" Value="8"/>
          <Setter Property="MinWidth" Value="8"/>
          <Setter Property="Background" Value="Transparent"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ScrollBar">
                <Track x:Name="PART_Track" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" IsDirectionReversed="True" Margin="1,2">
                  <Track.Thumb>
                    <Thumb>
                      <Thumb.Template>
                        <ControlTemplate TargetType="Thumb">
                          <Border Background="{DynamicResource Brush.Thumb}" CornerRadius="3"/>
                        </ControlTemplate>
                      </Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                </Track>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
          <Style.Triggers>
            <Trigger Property="Orientation" Value="Horizontal">
              <Setter Property="Height" Value="0"/>
            </Trigger>
          </Style.Triggers>
        </Style>
        """);

    /// <summary>The title strip's minimize / close: flat, a light veil on hover (as in the history window).</summary>
    private static Style FlatButtonStyle()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), "Bd"));
        template.Triggers.Add(hover);
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(TemplateProperty, template));
        return style;
    }
}
