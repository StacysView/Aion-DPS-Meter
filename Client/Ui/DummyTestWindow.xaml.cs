using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Combat;
using AionDPS.History;

namespace AionDPS.Ui;

/// <summary>
/// The dummy test: set up a timed run at a training dummy (<see cref="DummyTestController"/> does the clock, the capture and the store), keep the runs,
/// pick a configuration A and a configuration B among them and read what changed, then copy or save the table. All wording comes from
/// <see cref="DummyTestText"/> and ui_strings.json (DummyTest.*). The window never changes the analysis: it shows <c>DummyTestComparison</c> and the level
/// curves as the controller returns them. The controller's events come from the thread that polls the capture, so they are marshalled here.
/// The idea of a window for a timed dummy test is SkeeveTV's (Ui/DummyTestWindow.cs, SkeeveAN/Aion-DPS-Meter, MIT, (c) 2026 SkeeveTV);
/// this one is built on our controller, with skill levels and statistics.
/// </summary>
public partial class DummyTestWindow : Window
{
    private static readonly Brush Green = PetLook.Frozen(Color.FromRgb(0x5B, 0xD7, 0x5B));
    private static readonly Brush Red = PetLook.Frozen(Color.FromRgb(0xFF, 0x5A, 0x4F));
    private static readonly Brush GreenTint = PetLook.Frozen(Color.FromArgb(0x1C, 0x5B, 0xD7, 0x5B));
    private static readonly Brush RedTint = PetLook.Frozen(Color.FromArgb(0x1C, 0xFF, 0x5A, 0x4F));
    private static readonly Brush GreyTint = PetLook.Frozen(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));

    /// <summary>A small framed word on a run or a tier ("Incomplete", "Levels confirmed").</summary>
    private sealed record Badge(string Text, Brush Brush);

    /// <summary>One line of the setup checklist.</summary>
    private sealed record CheckItem(string Glyph, Brush Brush, string Text);

    /// <summary>A setup name shared by several runs, with its two buttons (to A, to B).</summary>
    private sealed record LabelGroup(string Label, string Title);

    private sealed record SkillRowView(
        ImageSource? Icon, string Skill, string Level, Brush LevelBrush, string Uses, string PlainA, string PlainB, string Change, string Interval,
        string Crit, Brush CritBrush, string CritTip, string Verdict, Brush VerdictBrush, Brush RowBack, double RowOpacity);

    private sealed record RankRowView(
        string Rank, ImageSource? Icon, string Skill, string Levels, string Gain, Brush GainBrush, string Relative, string Tier, Brush TierBrush, string Note);

    /// <summary>A stored run in the list, with the four boxes (A, B, C, D) that put it in a configuration: never in two. The boxes bind to this.</summary>
    private sealed class RunItem : INotifyPropertyChanged
    {
        private readonly Action _changed;
        private int _slot = -1;

        public RunItem(DummyRunRow row, string title, string sub, string dps, IReadOnlyList<Badge> badges, Action changed)
        {
            Row = row;
            Title = title;
            Sub = sub;
            Dps = dps;
            Badges = badges;
            _changed = changed;
        }

        public DummyRunRow Row { get; }

        public string Title { get; }

        public string Sub { get; }

        public string Dps { get; }

        public IReadOnlyList<Badge> Badges { get; }

        /// <summary>A run stopped early is never compared: its boxes are off.</summary>
        public bool CanCompare => Row.Complete;

        /// <summary>The configuration this run is in: 0 A, 1 B, 2 C, 3 D, -1 none.</summary>
        public int Slot => _slot;

        public bool IsA { get => _slot == 0; set => Choose(0, value); }

        public bool IsB { get => _slot == 1; set => Choose(1, value); }

        public bool IsC { get => _slot == 2; set => Choose(2, value); }

        public bool IsD { get => _slot == 3; set => Choose(3, value); }

        private void Choose(int slot, bool on)
        {
            int next = on ? slot : _slot == slot ? -1 : _slot;
            if (next == _slot)
            {
                return;
            }

            Put(next);
            _changed();
        }

        /// <summary>Sets the slot without telling the window (the caller does, once).</summary>
        public void Put(int slot)
        {
            _slot = slot;
            foreach (string name in new[] { nameof(IsA), nameof(IsB), nameof(IsC), nameof(IsD) })
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly DummyTestController _engine;
    private readonly DummyTestOverlay? _overlay;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<int, ImageSource?> _icons = new();
    private List<RunItem> _items = new();
    private IReadOnlyList<DummyRunRow>? _shownRuns;
    private DummyTestComparison? _comparison;
    private DummyMultiComparison? _multi;
    private List<(int Slot, List<RunItem> Items)> _active = new();
    private int _partner = 1;
    private IReadOnlyList<DummyLevelCurve> _curves = Array.Empty<DummyLevelCurve>();
    private string _sideAText = "", _sideBText = "", _checkKey = "";
    private bool _ready, _closed, _resultsDirty = true;

    public DummyTestWindow(DummyTestController engine, DummyTestOverlay? overlay)
    {
        InitializeComponent();
        // WPF formats bound dates and numbers in en-US unless told otherwise.
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        ThemedChrome.Apply(this);
        _engine = engine;
        _overlay = overlay;

        BuildPresets();
        bool overlayUsable = overlay is { IsClosed: false };
        OverlayToggle.IsChecked = overlayUsable && overlay!.ShownByUser;
        OverlayToggle.IsEnabled = overlayUsable;
        MoveToggle.IsEnabled = overlayUsable;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => RefreshLive();
        _engine.Changed += OnEngineChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _engine.Changed -= OnEngineChanged;
            if (_overlay is { IsClosed: false })
            {
                _overlay.SetMoveMode(false);
            }
        };

        _ready = true;
        ShowTab("Setup");
        RefreshRuns();
        RefreshLive();
        _timer.Start();
    }

    // ---------------------------------------------------------------- plumbing

    // The controller raises Changed on the thread that polls the capture.
    private void OnEngineChanged()
    {
        if (!_closed)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_closed)
                {
                    return;
                }

                RefreshRuns();
                RefreshLive();
            }));
        }
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private void BuildPresets()
    {
        var style = (Style)FindResource("PresetChip");
        foreach (int seconds in DummyTestText.PresetSeconds)
        {
            PresetPanel.Children.Add(new RadioButton
            {
                Style = style,
                GroupName = "Length",
                Tag = seconds,
                Content = DummyTestText.Duration(TimeSpan.FromSeconds(seconds)),
                IsChecked = seconds == 60,
            });
        }
    }

    private TimeSpan SelectedLength() =>
        PresetPanel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag is int seconds
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMinutes(1);

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        // The XAML ticks nothing, but Checked can still fire while the window is being built.
        if (_ready && sender is RadioButton { Tag: string tab })
        {
            ShowTab(tab);
        }
    }

    private void ShowTab(string tab)
    {
        SetupPanel.Visibility = tab == "Setup" ? Visibility.Visible : Visibility.Collapsed;
        RunsPanel.Visibility = tab == "Runs" ? Visibility.Visible : Visibility.Collapsed;
        ResultsPanel.Visibility = tab == "Results" ? Visibility.Visible : Visibility.Collapsed;
        ExportPanel.Visibility = tab == "Export" ? Visibility.Visible : Visibility.Collapsed;
        RadioButton button = tab switch
        {
            "Runs" => TabRuns,
            "Results" => TabResults,
            "Export" => TabExport,
            _ => TabSetup,
        };
        if (button.IsChecked != true)
        {
            button.IsChecked = true; // Checked comes back here once, with the tab already shown
        }

        SetStatus("");
        if (tab == "Results")
        {
            RenderResults();
        }
        else if (tab == "Export")
        {
            RenderExport();
        }
    }

    // ---------------------------------------------------------------- setup page

    private void OnRunClicked(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        switch (_engine.State)
        {
            case DummyState.Armed:
                _engine.Cancel();
                SetStatus("");
                break;
            case DummyState.Running:
                _engine.Stop();
                SetStatus(loc["DummyTest.Run.StoppedNote"]);
                break;
            default:
                if (!_engine.Arm(SelectedLength(), LabelBox.Text.Trim(), NoteBox.Text.Trim()))
                {
                    SetStatus(loc["DummyTest.Run.Refused"]);
                }
                else
                {
                    SetStatus(_engine.SkillsReadInfo.Confirmed ? "" : loc["DummyTest.Run.Unconfirmed"]);
                }

                break;
        }

        RefreshLive();
    }

    private void OnPrepareClicked(object sender, RoutedEventArgs e)
    {
        _engine.RequestSkillConfirmation();
        SetStatus("");
        RefreshLive();
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        bool confirmed = _engine.ConfirmSkillsRead();
        SetStatus(confirmed ? "" : LocalizationManager.Instance["DummyTest.Skills.NotYet"]);
        RefreshLive();
    }

    private void OnOverlayToggled(object sender, RoutedEventArgs e) => _overlay?.SetShown(OverlayToggle.IsChecked == true);

    private void OnMoveToggled(object sender, RoutedEventArgs e) => _overlay?.SetMoveMode(MoveToggle.IsChecked == true);

    /// <summary>The state, the button, the checklist: everything on the setup page that moves with the clock. Cheap; called by a 500 ms timer and on every change.</summary>
    private void RefreshLive()
    {
        if (_closed)
        {
            return;
        }

        DummyTextEnv env = DummyTextEnv.Current;
        DummyState state = _engine.State;
        bool running = state == DummyState.Running || state == DummyState.Finished;
        DummyRun? run = running ? _engine.Current : null;

        RunButton.Content = env.Loc(state switch
        {
            DummyState.Armed => "DummyTest.Run.Cancel",
            DummyState.Running => "DummyTest.Run.Stop",
            _ => "DummyTest.Run.Start",
        });
        bool editable = state == DummyState.Idle || state == DummyState.Finished;
        PresetPanel.IsEnabled = editable;
        LabelBox.IsEnabled = editable;
        NoteBox.IsEnabled = editable;

        StateText.Text = DummyTestText.StateText(state, run?.Complete ?? true, env);
        DummyLive? live = run is null ? null : _engine.Live();
        string dps = DummyTestText.Number(live?.Dps ?? 0, env.Culture);
        int hits = live?.Hits ?? 0;
        StateDetail.Text = state switch
        {
            DummyState.Armed => env.Format("DummyTest.Detail.Armed", DummyTestText.Duration(_engine.Planned)),
            DummyState.Running => env.Format("DummyTest.Detail.Running", DummyTestText.Clock(_engine.Remaining(), env.Culture), dps, hits),
            DummyState.Finished when run is { Complete: false } => env.Format("DummyTest.Detail.Stopped", run.Number),
            DummyState.Finished when run is not null => env.Format("DummyTest.Detail.Finished", run.Number, dps, hits),
            _ => env.Loc("DummyTest.Detail.Idle"),
        };

        // The checklist, rebuilt only when a line changed (a list that is replaced every half second flickers).
        SkillsReadInfo info = _engine.SkillsReadInfo;
        var lines = new List<DummyStatusLine>
        {
            DummyTestText.TargetLine(running ? _engine.TargetName : "", running && _engine.TargetIsDummy, env),
            DummyTestText.SkillsLine(info, env),
            DummyTestText.StatsLine(_engine.LocalStats?.Values.Count ?? 0, env),
            DummyTestText.EffectsLine(_engine.ActiveEffectCount, env),
        };
        if (!_engine.HasStore)
        {
            lines.Add(new DummyStatusLine(DummyStatus.Warn, env.Format("DummyTest.Store.None", _engine.StoreError ?? "")));
        }
        else if (_engine.StoreError is { } error)
        {
            lines.Add(new DummyStatusLine(DummyStatus.Warn, env.Format("DummyTest.Store.Error", error)));
        }

        string key = string.Join("\n", lines.Select(l => (int)l.Status + l.Text));
        if (key != _checkKey)
        {
            _checkKey = key;
            Brush warn = Res("Brush.Warning", Brushes.Gold);
            Brush muted = Res("Brush.Muted", Brushes.Gray);
            CheckList.ItemsSource = lines.Select(l => new CheckItem(
                l.Status == DummyStatus.Ok ? "✓" : l.Status == DummyStatus.Warn ? "!" : "·",
                l.Status == DummyStatus.Ok ? Green : l.Status == DummyStatus.Warn ? warn : muted, l.Text)).ToList();
        }

        SkillsHintText.Text = DummyTestText.SkillsHint(info, env);
        ConfirmButton.IsEnabled = info.Requested;
    }

    // ---------------------------------------------------------------- runs page

    /// <summary>The list of stored runs, rebuilt when the controller has read it again; the boxes ticked and the chosen run stay.</summary>
    private void RefreshRuns()
    {
        IReadOnlyList<DummyRunRow> runs = _engine.Runs;
        if (ReferenceEquals(runs, _shownRuns))
        {
            return;
        }

        _shownRuns = runs;
        _resultsDirty = true;
        DummyTextEnv env = DummyTextEnv.Current;
        CultureInfo c = env.Culture;
        Dictionary<long, int> was = _items.Where(i => i.Slot >= 0).ToDictionary(i => i.Row.Id, i => i.Slot);
        long? chosen = (RunsList.SelectedItem as RunItem)?.Row.Id;

        Brush warn = Res("Brush.Warning", Brushes.Gold);
        Brush muted = Res("Brush.Muted", Brushes.Gray);
        var items = new List<RunItem>();
        foreach (DummyRunRow row in runs)
        {
            string name = row.Label.Length > 0 ? row.Label : env.Loc("DummyTest.Run.NoName");
            string length = row.Complete ? DummyTestText.Duration(row.Planned) : DummyTestText.Duration(row.Length) + " / " + DummyTestText.Duration(row.Planned);
            string when = row.StartedAt.ToString("d MMM", c) + " " + row.StartedAt.ToString("t", c);
            var badges = new List<Badge>();
            if (!row.Complete)
            {
                badges.Add(new Badge(env.Loc("DummyTest.Badge.Incomplete"), Red));
            }

            badges.Add(row.LevelsConfirmed ? new Badge(env.Loc("DummyTest.Badge.Confirmed"), Green) : new Badge(env.Loc("DummyTest.Badge.Unconfirmed"), warn));
            if (row.Note.Length > 0)
            {
                badges.Add(new Badge(env.Loc("DummyTest.Badge.Note"), muted));
            }

            var item = new RunItem(row, env.Format("DummyTest.Run.Title", row.Number, name), $"{when}  ·  {length}  ·  {(row.TargetName.Length > 0 ? row.TargetName : "-")}",
                DummyTestText.Number(DummyTestText.RunDps(row), c), badges, OnSidesChanged);
            item.Put(row.Complete && was.TryGetValue(row.Id, out int slot) ? slot : -1);
            items.Add(item);
        }

        _items = items;
        RunsList.ItemsSource = _items;
        RunsList.SelectedItem = chosen is long id ? _items.FirstOrDefault(i => i.Row.Id == id) : null;
        RunsEmpty.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // The names shared by two runs or more are setups of their own.
        var groups = runs.Where(r => r.Complete && r.Label.Length > 0).GroupBy(r => r.Label).Where(g => g.Count() > 1)
            .Select(g => new LabelGroup(g.Key, g.Key + " (" + DummyTestText.Runs(g.Count(), env) + ")")).ToList();
        LabelsList.ItemsSource = groups;
        LabelsBox.Visibility = groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateSides();
        ShowDetail(RunsList.SelectedItem as RunItem);
        if (ResultsPanel.Visibility == Visibility.Visible)
        {
            RenderResults();
        }
        else if (ExportPanel.Visibility == Visibility.Visible)
        {
            RenderExport();
        }
    }

    private void OnSidesChanged()
    {
        _resultsDirty = true;
        UpdateSides();
    }

    private static readonly string[] Letters = { "A", "B", "C", "D" };

    private Brush SideBrush(int slot) => TryFindResource("Side" + Letters[slot]) as Brush ?? Brushes.Gray;

    private List<RunItem> Side(int slot) => _items.Where(i => i.Slot == slot).OrderBy(i => i.Row.Number).ToList();

    private static string Describe(IReadOnlyList<RunItem> items, DummyTextEnv env)
    {
        if (items.Count == 0)
        {
            return env.Loc("DummyTest.Runs.None");
        }

        List<string> labels = items.Select(i => i.Row.Label).Where(l => l.Length > 0).Distinct().ToList();
        string who = labels.Count > 0 ? string.Join(" + ", labels) : string.Join(", ", items.Select(i => "#" + i.Row.Number.ToString(CultureInfo.InvariantCulture)));
        return who + " (" + DummyTestText.Runs(items.Count, env) + ")";
    }

    /// <summary>The name of a configuration without its run count: its labels, or its run numbers.</summary>
    private static string ShortName(IReadOnlyList<RunItem> items)
    {
        List<string> labels = items.Select(i => i.Row.Label).Where(l => l.Length > 0).Distinct().ToList();
        return labels.Count > 0 ? string.Join(" + ", labels) : string.Join(", ", items.Select(i => "#" + i.Row.Number.ToString(CultureInfo.InvariantCulture)));
    }

    private void UpdateSides()
    {
        DummyTextEnv env = DummyTextEnv.Current;
        var parts = new List<string>();
        for (int slot = 0; slot < Letters.Length; slot++)
        {
            List<RunItem> items = Side(slot);
            if (items.Count > 0)
            {
                parts.Add(env.Format("DummyTest.Runs.Side", Letters[slot], Describe(items, env)));
            }
        }

        SidesText.Text = parts.Count == 0 ? env.Loc("DummyTest.Runs.None") : string.Join("   ·   ", parts);
        CompareButton.IsEnabled = parts.Count >= 2;
    }

    private void OnClearSidesClicked(object sender, RoutedEventArgs e)
    {
        foreach (RunItem item in _items)
        {
            item.Put(-1);
        }

        OnSidesChanged();
    }

    private void OnLabelTo(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag, DataContext: LabelGroup group } && int.TryParse(tag, out int slot))
        {
            PutLabel(group, slot);
        }
    }

    /// <summary>Every complete run of a setup goes to one configuration (and leaves the others); whatever else was in that configuration leaves it.</summary>
    private void PutLabel(LabelGroup group, int slot)
    {
        foreach (RunItem item in _items)
        {
            if (item.CanCompare && item.Row.Label == group.Label)
            {
                item.Put(slot);
            }
            else if (item.Slot == slot)
            {
                item.Put(-1);
            }
        }

        OnSidesChanged();
    }

    private void OnCompareClicked(object sender, RoutedEventArgs e) => ShowTab("Results");

    private void OnRunSelected(object sender, SelectionChangedEventArgs e) => ShowDetail(RunsList.SelectedItem as RunItem);

    private void ShowDetail(RunItem? item)
    {
        DetailNone.Visibility = item is null ? Visibility.Visible : Visibility.Collapsed;
        DetailBox.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        if (item is null)
        {
            return;
        }

        DummyTextEnv env = DummyTextEnv.Current;
        DummyRunRow row = item.Row;
        DetailTitle.Text = item.Title;
        DetailFacts.Text = env.Format("DummyTest.Detail.Facts", item.Sub, DummyTestText.Number(row.TotalDamage, env.Culture));
        DetailLabel.Text = row.Label;
        DetailNote.Text = row.Note;
    }

    private void OnSaveDetailClicked(object sender, RoutedEventArgs e)
    {
        if (RunsList.SelectedItem is not RunItem item)
        {
            return;
        }

        string label = DetailLabel.Text.Trim(), note = DetailNote.Text.Trim();
        if (label != item.Row.Label)
        {
            _engine.UpdateLabel(item.Row.Id, label);
        }

        if (note != item.Row.Note)
        {
            _engine.UpdateNote(item.Row.Id, note);
        }

        SetStatus(LocalizationManager.Instance["DummyTest.Detail.Saved"]);
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (RunsList.SelectedItem is not RunItem item)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        if (MessageBox.Show(this, string.Format(loc["DummyTest.Detail.DeleteAsk"], item.Title), loc["DummyTest.Title"], MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _engine.Delete(item.Row.Id);
        SetStatus(loc["DummyTest.Detail.Deleted"]);
    }

    // ---------------------------------------------------------------- results page

    /// <summary>Compares the configurations in use (each with the first) and reads the level curves, once per change of the sides or of the runs.</summary>
    private void EnsureComputed()
    {
        if (!_resultsDirty)
        {
            return;
        }

        _resultsDirty = false;
        _active = Enumerable.Range(0, Letters.Length).Select(slot => (Slot: slot, Items: Side(slot))).Where(x => x.Items.Count > 0).ToList();
        _multi = null;
        _curves = Array.Empty<DummyLevelCurve>();
        try
        {
            if (_active.Count >= 2)
            {
                _multi = _engine.CompareMany(_active.Select(x => (IReadOnlyList<long>)x.Items.Select(i => i.Row.Id).ToList()).ToList());
            }

            _curves = _engine.LevelCurves();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The analysis is pure and should never throw; if it does, the window says so instead of taking the meter down.
            SetStatus(string.Format(LocalizationManager.Instance["DummyTest.Results.Failed"], ex.Message));
        }

        SelectPartner();
    }

    /// <summary>The configuration shown in detail against the first one (the second by default; the chips of the ranking choose another).</summary>
    private void SelectPartner()
    {
        DummyTextEnv env = DummyTextEnv.Current;
        _comparison = null;
        _sideAText = _sideBText = "";
        if (_multi is null || _active.Count < 2)
        {
            return;
        }

        _partner = Math.Clamp(_partner, 1, _active.Count - 1);
        _comparison = _multi.VersusFirst[_partner - 1];
        _sideAText = Describe(_active[0].Items, env);
        _sideBText = Describe(_active[_partner].Items, env);
    }

    /// <summary>The text environment of the pair shown in detail: "[A]" is the reference's letter, "[B]" the letter of the configuration compared with it.</summary>
    private DummyTextEnv PairEnv() => _active.Count >= 2
        ? DummyTextEnv.For(Letters[_active[0].Slot], Letters[_active[Math.Clamp(_partner, 1, _active.Count - 1)].Slot])
        : DummyTextEnv.Current;

    private ImageSource? IconFor(int baseId)
    {
        if (baseId == 0)
        {
            return null;
        }

        if (_icons.TryGetValue(baseId, out ImageSource? known))
        {
            return known;
        }

        ImageSource? icon = null;
        if (Aion2.Protocol.Aion2SkillIcons.PathFor(baseId) is string path)
        {
            try
            {
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path);
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 48;
                image.EndInit();
                image.Freeze();
                icon = image;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
            {
                // No readable icon: the line just has none.
            }
        }

        return _icons[baseId] = icon;
    }

    private void RenderResults()
    {
        EnsureComputed();
        DummyTextEnv env = DummyTextEnv.Current;
        DummyTextEnv penv = PairEnv();
        CultureInfo c = env.Culture;
        Brush warn = Res("Brush.Warning", Brushes.Gold);
        Brush muted = Res("Brush.Muted", Brushes.Gray);
        Brush faint = Res("Brush.Faint", Brushes.DimGray);
        Brush label = Res("Brush.Label", Brushes.LightGray);
        Brush body = Res("Brush.Body", Brushes.White);
        Brush heading = Res("Brush.Heading", Brushes.White);

        ResultsEmpty.Visibility = _comparison is null ? Visibility.Visible : Visibility.Collapsed;
        ComparisonBox.Visibility = _comparison is null ? Visibility.Collapsed : Visibility.Visible;
        if (_comparison is { } cmp)
        {
            ResultsHeader.Text = penv.Format("DummyTest.Results.Header", _sideAText, _sideBText)
                + (cmp.Comparable ? "  ·  " + DummyTestText.Duration(TimeSpan.FromSeconds(cmp.PlannedSeconds)) : "");
            DpsCaption.Text = penv.Loc("DummyTest.Results.Dps");
            HeadLevel.Text = penv.Loc("DummyTest.Col.Level");
            HeadPlainA.Text = penv.Loc("DummyTest.Col.PlainA");
            HeadPlainB.Text = penv.Loc("DummyTest.Col.PlainB");
            HeadCrit.Text = penv.Loc("DummyTest.Col.Crit");

            // A refused comparison says why in its own words; the other warnings stay in the list.
            DummyTestWarning? refusal = cmp.Comparable ? null
                : cmp.Warnings.FirstOrDefault(w => w.Kind is DummyTestWarningKind.NoCompleteRuns or DummyTestWarningKind.PlannedLengthsDiffer);
            RefusalText.Text = refusal is null ? "" : DummyTestText.WarningText(refusal, cmp, penv);
            RefusalText.Visibility = refusal is null ? Visibility.Collapsed : Visibility.Visible;
            ComparedBox.Visibility = cmp.Comparable ? Visibility.Visible : Visibility.Collapsed;

            VerdictText.Text = DummyTestText.Conclusion(cmp, penv);
            if (cmp.Dps is { } dps)
            {
                DpsHeadline.Text = DummyTestText.DpsHeadline(dps, penv);
                DpsHeadline.Foreground = dps.WithinNoise ? heading : dps.Delta > 0 ? Green : Red;
                DpsStatement.Text = DummyTestText.DpsStatement(cmp, penv);
            }

            SkillRows.ItemsSource = cmp.Rows.Select(r =>
            {
                Brush verdict = r.Verdict switch
                {
                    DummyTestVerdict.Better => Green,
                    DummyTestVerdict.Worse => Red,
                    DummyTestVerdict.NoMeasurableDifference => muted,
                    _ => faint,
                };
                return new SkillRowView(
                    IconFor(r.BaseId), penv.Skill(r.Skill), DummyTestText.Levels(r.LevelA, r.LevelB), r.LevelChanged ? warn : label,
                    DummyTestText.UsesPerMinute(r, c), DummyTestText.PlainMean(r.PlainMeanA, c), DummyTestText.PlainMean(r.PlainMeanB, c),
                    DummyTestText.Change(r, c), DummyTestText.Interval(r.PlainChangeLow, r.PlainChangeHigh, c),
                    DummyTestText.CritRate(r, c), r.CritRateDiffers ? warn : body, r.CritRateDiffers ? penv.Loc("DummyTest.Crit.Differs.Tip") : "",
                    penv.Loc(DummyTestText.VerdictKey(r.Verdict)), verdict,
                    r.Verdict == DummyTestVerdict.Better ? GreenTint : r.Verdict == DummyTestVerdict.Worse ? RedTint
                        : r.Verdict == DummyTestVerdict.NoMeasurableDifference ? GreyTint : Brushes.Transparent,
                    r.Verdict == DummyTestVerdict.NotEnoughData ? 0.55 : 1.0);
            }).ToList();

            List<string> warnings = DummyTestText.WarningTexts(cmp.Warnings.Where(w => !ReferenceEquals(w, refusal)), cmp, penv);
            WarningsList.ItemsSource = warnings;
            WarningsBox.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            RenderSides(env, c, muted, heading);
            RenderRotation(env, c, muted, body);
        }

        // The ranking does not depend on the configurations picked: every complete run with confirmed levels counts.
        RankingEmpty.Visibility = _curves.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RankingList.ItemsSource = _curves.Select((curve, index) => new RankRowView(
            (index + 1).ToString(CultureInfo.InvariantCulture) + ".", IconFor(curve.BaseId), env.Skill(curve.Skill),
            env.Format("DummyTest.Curve.Levels", DummyTestText.CurveLevels(curve)), DummyTestText.CurveGain(curve, env),
            curve.DpsGainPerLevel is double gain ? gain > 0 ? Green : gain < 0 ? Red : muted : muted,
            DummyTestText.CurveRelative(curve, env), DummyTestText.TierText(curve.Tier, env),
            curve.Tier == DummyCurveTier.Measured ? Green : curve.Tier == DummyCurveTier.TwoPoints ? warn : muted,
            DummyTestText.CurveNote(curve, env))).ToList();
    }

    // ---------------------------------------------------------------- several configurations and rotation

    /// <summary>A small lit square with the side's letter.</summary>
    private UIElement LetterChip(int slot) => new Border
    {
        Width = 26,
        Height = 24,
        CornerRadius = new CornerRadius(5),
        Background = SideBrush(slot),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = Letters[slot], FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x10, 0x16)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    /// <summary>Three or four configurations: each against the first, best DPS first, and the chips that choose the pair shown skill by skill.</summary>
    private void RenderSides(DummyTextEnv env, CultureInfo c, Brush muted, Brush heading)
    {
        bool show = _multi is { Sides.Count: >= 3 };
        SidesBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show || _multi is null)
        {
            return;
        }

        SidesSub.Text = env.Format("DummyTest.Sides.Sub", ShortName(_active[0].Items));
        SidesNote.Text = env.Loc("DummyTest.Sides.Many");
        SidesPanel.Children.Clear();
        foreach (int k in Enumerable.Range(0, _multi.Sides.Count).OrderByDescending(k => _multi.Sides[k].Runs > 0 ? _multi.Sides[k].Dps : -1))
        {
            DummySideSummary side = _multi.Sides[k];
            int slot = _active[k].Slot;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            UIElement chip = LetterChip(slot);
            ((FrameworkElement)chip).Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Add(chip);
            var name = new TextBlock { Text = Describe(_active[k].Items, env), FontWeight = FontWeights.SemiBold, Foreground = heading, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            var dpsText = new TextBlock { Text = side.Runs > 0 ? DummyTestText.Number(side.Dps, c) + " DPS" : "-", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(dpsText, 2);
            grid.Children.Add(dpsText);

            string changeText = "", verdictText = "";
            Brush brush = muted;
            if (side.Runs == 0)
            {
                verdictText = env.Loc("DummyTest.Sides.NoRun");
            }
            else if (k == 0)
            {
                verdictText = env.Loc("DummyTest.Sides.Reference");
            }
            else if (_multi.VersusFirst[k - 1] is { Comparable: true, Dps: { } d })
            {
                changeText = (d.DeltaFraction is double f ? DummyTestText.Percent(f, c) + "  " : "") + "(" + DummyTestText.Signed(d.Delta, c) + " DPS)";
                verdictText = env.Loc(d.WithinNoise ? "DummyTest.Sides.Within" : "DummyTest.Sides.Beyond");
                brush = d.WithinNoise ? muted : d.Delta > 0 ? Green : Red;
            }
            else
            {
                verdictText = env.Loc("DummyTest.Sides.NotComparable");
            }

            var change = new TextBlock { Text = changeText, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(change, 3);
            grid.Children.Add(change);
            var verdict = new TextBlock { Text = verdictText, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(verdict, 4);
            grid.Children.Add(verdict);
            SidesPanel.Children.Add(new Border { Background = GreyTint, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 4), Child = grid });
        }

        PartnerChips.Children.Clear();
        PartnerChips.Children.Add(new TextBlock { Text = env.Format("DummyTest.Sides.Detail", Letters[_active[0].Slot]), Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) });
        for (int k = 1; k < _active.Count; k++)
        {
            int partner = k;
            int slot = _active[k].Slot;
            var chip = new ToggleButton { Style = (Style)FindResource("SideChip"), Content = Letters[slot], Tag = SideBrush(slot), IsChecked = k == _partner };
            chip.Click += (_, _) =>
            {
                _partner = partner;
                SelectPartner();
                RenderResults();
            };
            PartnerChips.Children.Add(chip);
        }
    }

    /// <summary>The opener of each configuration and its DPS over time, side by side (descriptions: the verdict is the total DPS above).</summary>
    private void RenderRotation(DummyTextEnv env, CultureInfo c, Brush muted, Brush body)
    {
        RotationPanel.Children.Clear();
        if (_multi is null)
        {
            RotationBox.Visibility = Visibility.Collapsed;
            return;
        }

        RotationBox.Visibility = Visibility.Visible;
        Style card = (Style)FindResource("Card");
        Style caption = (Style)FindResource("Caption");

        // The openers.
        var openers = new StackPanel();
        openers.Children.Add(new TextBlock { Text = env.Loc("DummyTest.Rotation.Opener"), Style = caption, Margin = new Thickness(0, 0, 0, 6) });
        for (int k = 0; k < _multi.Sides.Count; k++)
        {
            DummySideSummary side = _multi.Sides[k];
            var line = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(LetterChip(_active[k].Slot));
            head.Children.Add(new TextBlock { Text = Describe(_active[k].Items, env), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            line.Children.Add(head);
            line.Children.Add(new TextBlock { Text = side.Runs == 0 ? env.Loc("DummyTest.Sides.NoRun") : DummyTestText.OpenerText(side.Rotation, env), Foreground = body, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(34, 3, 0, 0) });
            if (DummyTestText.OpenerVaries(side.Rotation))
            {
                line.Children.Add(new TextBlock { Text = env.Loc("DummyTest.Rotation.Varies"), Foreground = muted, FontSize = 11.5, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(34, 2, 0, 0) });
            }

            openers.Children.Add(line);
        }

        RotationPanel.Children.Add(new Border { Style = card, Margin = new Thickness(0, 0, 0, 10), Child = openers });

        // DPS over time: one column per configuration, the best of each slice lit.
        var curve = new StackPanel();
        curve.Children.Add(new TextBlock { Text = env.Loc("DummyTest.Rotation.Curve"), Style = caption, Margin = new Thickness(0, 0, 0, 6) });
        List<int> withRuns = Enumerable.Range(0, _multi.Sides.Count).Where(k => _multi.Sides[k].Runs > 0).ToList();
        bool sameShape = withRuns.Count > 0 && withRuns.All(k => _multi.Sides[k].Rotation.Curve.Count == _multi.Sides[withRuns[0]].Rotation.Curve.Count
            && _multi.Sides[k].Rotation.Curve.Zip(_multi.Sides[withRuns[0]].Rotation.Curve, (x, y) => x.ToSeconds == y.ToSeconds).All(same => same));
        if (!sameShape)
        {
            curve.Children.Add(new TextBlock { Text = env.Loc("DummyTest.Rotation.CurveMixed"), Foreground = muted, TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            foreach (int unused in withRuns)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            int slices = _multi.Sides[withRuns[0]].Rotation.Curve.Count;
            for (int row = 0; row <= slices + 1; row++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int col = 0; col < withRuns.Count; col++)
            {
                UIElement chip = LetterChip(_active[withRuns[col]].Slot);
                ((FrameworkElement)chip).HorizontalAlignment = HorizontalAlignment.Right;
                ((FrameworkElement)chip).Margin = new Thickness(0, 0, 0, 4);
                Grid.SetColumn(chip, col + 1);
                grid.Children.Add(chip);
            }

            for (int i = 0; i < slices; i++)
            {
                var slice = new TextBlock { Text = DummyTestText.SliceLabel(_multi.Sides[withRuns[0]].Rotation.Curve[i], env), Foreground = muted, Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetRow(slice, i + 1);
                grid.Children.Add(slice);
                double best = withRuns.Max(k => _multi.Sides[k].Rotation.Curve[i].Dps);
                bool unique = withRuns.Count(k => _multi.Sides[k].Rotation.Curve[i].Dps >= best) == 1;
                for (int col = 0; col < withRuns.Count; col++)
                {
                    double value = _multi.Sides[withRuns[col]].Rotation.Curve[i].Dps;
                    bool lit = unique && value >= best && withRuns.Count > 1;
                    var cell = new TextBlock
                    {
                        Text = DummyTestText.Number(value, c),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        FontWeight = lit ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = lit ? Green : body,
                        Margin = new Thickness(0, 2, 0, 2),
                    };
                    Grid.SetRow(cell, i + 1);
                    Grid.SetColumn(cell, col + 1);
                    grid.Children.Add(cell);
                }
            }

            var total = new TextBlock { Text = env.Loc("DummyTest.Rotation.Total"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
            Grid.SetRow(total, slices + 1);
            grid.Children.Add(total);
            for (int col = 0; col < withRuns.Count; col++)
            {
                var cell = new TextBlock { Text = DummyTestText.Number(_multi.Sides[withRuns[col]].Dps, c), HorizontalAlignment = HorizontalAlignment.Right, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
                Grid.SetRow(cell, slices + 1);
                Grid.SetColumn(cell, col + 1);
                grid.Children.Add(cell);
            }

            curve.Children.Add(grid);
        }

        RotationPanel.Children.Add(new Border { Style = card, Child = curve });
    }

    // ---------------------------------------------------------------- export page

    private string ExportText()
    {
        EnsureComputed();
        return _comparison is { Comparable: true } cmp ? DummyTestText.DiscordTable(cmp, PairEnv(), _sideAText, _sideBText) : "";
    }

    private void RenderExport()
    {
        string text = ExportText();
        bool any = text.Length > 0;
        ExportPreview.Text = text;
        ExportPreview.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        ExportEmpty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        CopyButton.IsEnabled = any;
        CsvButton.IsEnabled = any;
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        string text = ExportText();
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            SetStatus(LocalizationManager.Instance["DummyTest.Export.Copied"]);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The clipboard is one system-wide resource and any other process can hold it open for a moment.
            SetStatus(LocalizationManager.Instance["DummyTest.Export.ClipboardBusy"]);
        }
    }

    private void OnSaveCsvClicked(object sender, RoutedEventArgs e)
    {
        EnsureComputed();
        if (_comparison is not { Comparable: true } cmp)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = loc["DummyTest.Export.Csv"],
            Filter = "CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = "dummy-test-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture) + ".csv",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            // A byte order mark so a spreadsheet reads the accents of a skill name right.
            File.WriteAllText(dialog.FileName, DummyTestText.Csv(cmp), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            SetStatus(string.Format(loc["DummyTest.Export.Saved"], dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(string.Format(loc["DummyTest.Export.SaveFailed"], ex.Message));
        }
    }
}
