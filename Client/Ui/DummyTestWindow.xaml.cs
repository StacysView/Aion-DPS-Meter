using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
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

    /// <summary>A stored run in the list, with the two boxes that put it in A or in B (never both). The boxes bind to this.</summary>
    private sealed class RunItem : INotifyPropertyChanged
    {
        private readonly Action _changed;
        private bool _a, _b;

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

        public bool IsA
        {
            get => _a;
            set
            {
                if (_a == value)
                {
                    return;
                }

                Put(value, false);
                _changed();
            }
        }

        public bool IsB
        {
            get => _b;
            set
            {
                if (_b == value)
                {
                    return;
                }

                Put(false, value);
                _changed();
            }
        }

        /// <summary>Sets both boxes without telling the window (the caller does, once).</summary>
        public void Put(bool a, bool b)
        {
            _a = a;
            _b = b;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsA)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsB)));
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
        HashSet<long> wasA = _items.Where(i => i.IsA).Select(i => i.Row.Id).ToHashSet();
        HashSet<long> wasB = _items.Where(i => i.IsB).Select(i => i.Row.Id).ToHashSet();
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
            item.Put(row.Complete && wasA.Contains(row.Id), row.Complete && wasB.Contains(row.Id));
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

    private List<RunItem> Side(bool a) => _items.Where(i => a ? i.IsA : i.IsB).OrderBy(i => i.Row.Number).ToList();

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

    private void UpdateSides()
    {
        DummyTextEnv env = DummyTextEnv.Current;
        List<RunItem> a = Side(true), b = Side(false);
        SidesText.Text = env.Format("DummyTest.Runs.Sides", Describe(a, env), Describe(b, env));
        CompareButton.IsEnabled = a.Count > 0 && b.Count > 0;
    }

    private void OnClearSidesClicked(object sender, RoutedEventArgs e)
    {
        foreach (RunItem item in _items)
        {
            item.Put(false, false);
        }

        OnSidesChanged();
    }

    private void OnLabelToA(object sender, RoutedEventArgs e) => PutLabel((sender as FrameworkElement)?.DataContext as LabelGroup, toA: true);

    private void OnLabelToB(object sender, RoutedEventArgs e) => PutLabel((sender as FrameworkElement)?.DataContext as LabelGroup, toA: false);

    /// <summary>Every complete run of a setup goes to one side (and leaves the other); whatever else was on that side leaves it.</summary>
    private void PutLabel(LabelGroup? group, bool toA)
    {
        if (group is null)
        {
            return;
        }

        foreach (RunItem item in _items)
        {
            if (item.CanCompare && item.Row.Label == group.Label)
            {
                item.Put(toA, !toA);
            }
            else if (toA ? item.IsA : item.IsB)
            {
                item.Put(false, false);
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

    /// <summary>Compares A with B and reads the level curves, once per change of the sides or of the runs.</summary>
    private void EnsureComputed()
    {
        if (!_resultsDirty)
        {
            return;
        }

        _resultsDirty = false;
        DummyTextEnv env = DummyTextEnv.Current;
        List<RunItem> a = Side(true), b = Side(false);
        _sideAText = Describe(a, env);
        _sideBText = Describe(b, env);
        _comparison = null;
        _curves = Array.Empty<DummyLevelCurve>();
        try
        {
            if (a.Count > 0 && b.Count > 0)
            {
                _comparison = _engine.Compare(a.Select(i => i.Row.Id).ToList(), b.Select(i => i.Row.Id).ToList());
            }

            _curves = _engine.LevelCurves();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The analysis is pure and should never throw; if it does, the window says so instead of taking the meter down.
            SetStatus(string.Format(LocalizationManager.Instance["DummyTest.Results.Failed"], ex.Message));
        }
    }

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
            ResultsHeader.Text = env.Format("DummyTest.Results.Header", _sideAText, _sideBText)
                + (cmp.Comparable ? "  ·  " + DummyTestText.Duration(TimeSpan.FromSeconds(cmp.PlannedSeconds)) : "");

            // A refused comparison says why in its own words; the other warnings stay in the list.
            DummyTestWarning? refusal = cmp.Comparable ? null
                : cmp.Warnings.FirstOrDefault(w => w.Kind is DummyTestWarningKind.NoCompleteRuns or DummyTestWarningKind.PlannedLengthsDiffer);
            RefusalText.Text = refusal is null ? "" : DummyTestText.WarningText(refusal, cmp, env);
            RefusalText.Visibility = refusal is null ? Visibility.Collapsed : Visibility.Visible;
            ComparedBox.Visibility = cmp.Comparable ? Visibility.Visible : Visibility.Collapsed;

            VerdictText.Text = DummyTestText.Conclusion(cmp, env);
            if (cmp.Dps is { } dps)
            {
                DpsHeadline.Text = DummyTestText.DpsHeadline(dps, env);
                DpsHeadline.Foreground = dps.WithinNoise ? heading : dps.Delta > 0 ? Green : Red;
                DpsStatement.Text = DummyTestText.DpsStatement(cmp, env);
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
                    IconFor(r.BaseId), env.Skill(r.Skill), DummyTestText.Levels(r.LevelA, r.LevelB), r.LevelChanged ? warn : label,
                    DummyTestText.UsesPerMinute(r, c), DummyTestText.PlainMean(r.PlainMeanA, c), DummyTestText.PlainMean(r.PlainMeanB, c),
                    DummyTestText.Change(r, c), DummyTestText.Interval(r.PlainChangeLow, r.PlainChangeHigh, c),
                    DummyTestText.CritRate(r, c), r.CritRateDiffers ? warn : body, r.CritRateDiffers ? env.Loc("DummyTest.Crit.Differs.Tip") : "",
                    env.Loc(DummyTestText.VerdictKey(r.Verdict)), verdict,
                    r.Verdict == DummyTestVerdict.Better ? GreenTint : r.Verdict == DummyTestVerdict.Worse ? RedTint
                        : r.Verdict == DummyTestVerdict.NoMeasurableDifference ? GreyTint : Brushes.Transparent,
                    r.Verdict == DummyTestVerdict.NotEnoughData ? 0.55 : 1.0);
            }).ToList();

            List<string> warnings = DummyTestText.WarningTexts(cmp.Warnings.Where(w => !ReferenceEquals(w, refusal)), cmp, env);
            WarningsList.ItemsSource = warnings;
            WarningsBox.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // The ranking does not depend on A and B: every complete run with confirmed levels counts.
        RankingEmpty.Visibility = _curves.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RankingList.ItemsSource = _curves.Select((curve, index) => new RankRowView(
            (index + 1).ToString(CultureInfo.InvariantCulture) + ".", IconFor(curve.BaseId), env.Skill(curve.Skill),
            env.Format("DummyTest.Curve.Levels", DummyTestText.CurveLevels(curve)), DummyTestText.CurveGain(curve, env),
            curve.DpsGainPerLevel is double gain ? gain > 0 ? Green : gain < 0 ? Red : muted : muted,
            DummyTestText.CurveRelative(curve, env), DummyTestText.TierText(curve.Tier, env),
            curve.Tier == DummyCurveTier.Measured ? Green : curve.Tier == DummyCurveTier.TwoPoints ? warn : muted,
            DummyTestText.CurveNote(curve, env))).ToList();
    }

    // ---------------------------------------------------------------- export page

    private string ExportText()
    {
        EnsureComputed();
        return _comparison is { Comparable: true } cmp ? DummyTestText.DiscordTable(cmp, DummyTextEnv.Current, _sideAText, _sideBText) : "";
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
