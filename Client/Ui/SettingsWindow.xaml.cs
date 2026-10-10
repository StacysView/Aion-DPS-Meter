using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AionDPS.Ui;

public partial class SettingsWindow : Window
{
    private readonly MeterSettings _settings;

    public SettingsWindow(MeterSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        // Same saved-size pattern as MainWindow's own RestoreWindowGeometry/SaveWindowGeometry.
        // Position stays CenterOwner (see the XAML), not restored here.
        if (settings.SettingsWindowWidth is double width && settings.SettingsWindowHeight is double height)
        {
            Width = width;
            Height = height;
        }

        // Same GDI decode as MainWindow's own AppIconImage - see its remarks on why a plain
        // pack://siteoforigin Source doesn't render this specific .ico at all.
        try
        {
            using var appIcon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "assets", "app", "aiondps.ico"));
            AppIconImage.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                appIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
        }

        CheckForUpdatesBox.IsChecked = settings.CheckForUpdates;
        AutoUploadProfileBox.IsChecked = settings.AutoUploadProfile;
        SelectComboItem(ThemeBox, settings.Theme);
        SelectComboItem(FontSizeBox, settings.FontSize);
        AlwaysOnTopBox.IsChecked = settings.AlwaysOnTopOnStartup;
        // Reflects the REAL registry state, not the last value this dialog wrote - see
        // OnStartWithWindowsChanged's own remarks.
        StartWithWindowsBox.IsChecked = StartupRegistration.IsEnabled();
        ShowShareBarsBox.IsChecked = settings.ShowShareBars;
        CompactOverlayBox.IsChecked = settings.CompactOverlay;
        AutoResetBox.IsChecked = settings.AutoReset;
        Loaded += (_, _) => ShowDiagnosticState();
        CurrentVersionText.Text = string.Format(LocalizationManager.Instance["Settings.Update.Current"], AionDPS.Update.AppVersion.Text);
        AutoResetSecondsBox.Text = Math.Clamp(settings.AutoResetSeconds, 1, 600).ToString();
        ShowBossHpBox.IsChecked = settings.ShowBossHp;
        ShowRiftTimerBox.IsChecked = settings.ShowRiftTimer;
        ShowShugoTimerBox.IsChecked = settings.ShowShugoTimer;
        TeaseLowDpsBox.IsChecked = settings.TeaseLowDps;
        TeaseBelowDpsBox.Text = Math.Clamp(settings.TeaseBelowDps, 0, 9_999_999).ToString();
        TeaseWhoBox.SelectedIndex = settings.TeaseWholeGroup ? 0 : 1;
        OverlayOpacitySlider.Value = settings.OverlayOpacity;
        var hotkeys = settings.EffectiveHotkeys();
        foreach (TextBox box in HotkeyBoxes())
        {
            box.Text = hotkeys[(MeterHotkey)Enum.Parse(typeof(MeterHotkey), (string)box.Tag)].ToString();
        }
        ShowDamageTakenBox.IsChecked = settings.ShowDamageTaken;
        RecordFightHistoryBox.IsChecked = settings.RecordFightHistory;
        PopulateCaptureAdapters(settings.CaptureAdapterId);
        Aion2CharacterNameBox.Text = settings.Aion2CharacterName ?? "";

        // Built from LocalizationManager.SupportedLanguages rather than hardcoded in XAML. Each item's
        // Content is the language's OWN native name - a real language picker never translates its
        // own entries.
        foreach (var (code, nativeName) in LocalizationManager.SupportedLanguages)
        {
            LanguageBox.Items.Add(new ComboBoxItem { Content = nativeName, Tag = code });
        }

        SelectComboItem(LanguageBox, LocalizationManager.Instance.Language);
    }

    /// <summary>Automatic (the adapter Windows routes internet traffic through), all adapters, then
    /// every live adapter with the recommended one starred - a gaming VPN shows up as its own entry.</summary>
    private void PopulateCaptureAdapters(string? saved)
    {
        var adapters = AionDPS.Aion2.Capture.CaptureAdapters.List();
        string auto = AionDPS.Aion2.Capture.CaptureAdapters.Recommended(adapters) is { } rec ? $"Automatic ({rec.Name}, {rec.Ipv4})" : "Automatic";
        CaptureAdapterBox.Items.Add(new ComboBoxItem { Content = auto, Tag = "" });
        CaptureAdapterBox.Items.Add(new ComboBoxItem { Content = "All adapters", Tag = AionDPS.Aion2.Capture.CaptureAdapters.AllAdapters });
        foreach (var adapter in adapters)
        {
            CaptureAdapterBox.Items.Add(new ComboBoxItem { Content = adapter.Label, Tag = adapter.Id, ToolTip = adapter.Description });
        }

        CaptureAdapterBox.SelectedItem = CaptureAdapterBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == (saved ?? "")) ?? CaptureAdapterBox.Items[0];
    }

    /// <summary>Matches by Tag, not Content: Content is now a {local:Loc ...} binding (so it reads
    /// as "Dunkel"/"Ciemny"/... depending on the current GUI language), while Tag stays the fixed,
    /// language-independent value ("Dark") that MeterSettings actually stores -- see
    /// ThemeBox's/FontSizeBox's XAML.</summary>
    private static void SelectComboItem(ComboBox box, string tag)
    {
        foreach (var item in box.Items)
        {
            if (item is ComboBoxItem cbi && string.Equals(cbi.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = cbi;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    /// <summary>Live preview, not just a save-time value: picking a language repaints every open
    /// window's {local:Loc ...} bindings immediately (see LocalizationManager.Language's remarks),
    /// so the effect of the choice is visible before Save/Cancel is even clicked -- unlike
    /// Theme/FontSize just below, which only apply once Save is pressed. Persisted to
    /// MeterSettings only in OnSaveClicked; clicking Cancel after changing the language leaves
    /// LocalizationManager changed for the rest of this run (nothing reverts it), but does not
    /// write a new default for the next launch.</summary>
    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedItem is ComboBoxItem { Tag: string code })
        {
            LocalizationManager.Instance.Language = code;
        }
    }

    /// <summary>Not deferred to Save, unlike every checkbox around it -- writes the HKCU Run-key
    /// entry right away (StartupRegistration.cs), so the effect matches what the checkbox shows
    /// even if the user then clicks Cancel. Checked/Unchecked fires AFTER IsChecked has already
    /// flipped, so this reads the new state directly.</summary>
    private void OnStartWithWindowsChanged(object sender, RoutedEventArgs e)
    {
        bool enable = StartWithWindowsBox.IsChecked == true;
        try
        {
            StartupRegistration.SetEnabled(enable);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            StartWithWindowsBox.IsChecked = !enable;
            MessageBox.Show(this, $"Could not update the Windows startup setting.\n\n{ex.Message}",
                "Start with Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!HotkeysAreDistinct())
        {
            return;
        }

        _settings.CheckForUpdates = CheckForUpdatesBox.IsChecked ?? true;
        _settings.AutoUploadProfile = AutoUploadProfileBox.IsChecked ?? true;
        _settings.Theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.Theme;
        _settings.FontSize = (FontSizeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.FontSize;
        _settings.Language = LocalizationManager.Instance.Language;
        _settings.AlwaysOnTopOnStartup = AlwaysOnTopBox.IsChecked ?? false;
        _settings.ShowShareBars = ShowShareBarsBox.IsChecked ?? true;
        _settings.CompactOverlay = CompactOverlayBox.IsChecked ?? true;
        _settings.AutoReset = AutoResetBox.IsChecked ?? true;
        _settings.AutoResetSeconds = int.TryParse(AutoResetSecondsBox.Text, out int seconds) ? Math.Clamp(seconds, 1, 600) : 10;
        _settings.ShowBossHp = ShowBossHpBox.IsChecked ?? false;
        _settings.ShowRiftTimer = ShowRiftTimerBox.IsChecked ?? true;
        _settings.ShowShugoTimer = ShowShugoTimerBox.IsChecked ?? true;
        _settings.TeaseLowDps = TeaseLowDpsBox.IsChecked ?? true;
        _settings.TeaseBelowDps = int.TryParse(TeaseBelowDpsBox.Text, out int teaseBelow) ? Math.Clamp(teaseBelow, 0, 9_999_999) : 13000;
        _settings.TeaseWholeGroup = TeaseWhoBox.SelectedIndex != 1;
        _settings.OverlayOpacity = OverlayOpacitySlider.Value;
        _settings.Hotkeys = HotkeyBoxes().ToDictionary(box => (MeterHotkey)Enum.Parse(typeof(MeterHotkey), (string)box.Tag), box => box.Text);
        _settings.ShowDamageTaken = ShowDamageTakenBox.IsChecked ?? false;
        _settings.RecordFightHistory = RecordFightHistoryBox.IsChecked ?? true;
        _settings.Aion2CharacterName = string.IsNullOrWhiteSpace(Aion2CharacterNameBox.Text) ? null : Aion2CharacterNameBox.Text.Trim();
        _settings.CaptureAdapterId = (CaptureAdapterBox.SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } adapterTag ? adapterTag : null;

        Saved?.Invoke();
        Close();
    }

    /// <summary>
    /// Fired on Save, right before Close() -- replaces the old ShowDialog()/DialogResult flow, per
    /// the user's request that Settings open as an independent second window instead of a modal
    /// blocking MainWindow (Show(), not ShowDialog(), from MainWindow.OnSettingsClicked). Setting
    /// DialogResult only works for a window actually shown via ShowDialog() -- doing it here would
    /// throw at runtime the moment Show() is used instead, hence this event instead.
    /// </summary>
    /// <summary>Starts or stops the diagnostic recording; returns the file while one is running,
    /// null when stopped. Set by the main window, which owns the capture.</summary>
    public Func<string?>? ToggleDiagnostic { get; set; }

    /// <summary>The file being recorded right now, if any.</summary>
    public Func<string?>? CurrentDiagnostic { get; set; }

    /// <summary>Run just before an update restarts the meter: files the fight on screen and saves the
    /// window position (MainWindow.PrepareForUpdateRestart).</summary>
    public Action? BeforeUpdateRestart { get; set; }

    private string? _lastDiagnostic;

    private void ShowDiagnosticState()
    {
        var loc = LocalizationManager.Instance;
        string? running = CurrentDiagnostic?.Invoke();
        DiagnosticButton.Content = loc[running is null ? "Settings.Diagnostic.Start" : "Settings.Diagnostic.Stop"];
        DiagnosticStatusLine.Text = running is not null ? string.Format(loc["Settings.Diagnostic.Running"], running)
            : _lastDiagnostic is not null ? string.Format(loc["Settings.Diagnostic.Saved"], _lastDiagnostic)
            : "";
    }

    private void OnDiagnosticClicked(object sender, RoutedEventArgs e)
    {
        string? before = CurrentDiagnostic?.Invoke();
        string? now = ToggleDiagnostic?.Invoke();
        if (before is not null && now is null)
        {
            _lastDiagnostic = before;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{before}\"") { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        ShowDiagnosticState();
    }

    /// <summary>One button: checks GitHub for a newer version and, when there is one, downloads it
    /// and restarts into it straight away (the process ends there).</summary>
    private async void OnCheckUpdateClicked(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        if (!AionDPS.Update.UpdateService.CanUpdate)
        {
            UpdateStatusLine.Text = loc["Settings.Update.NotInstalled"];
            return;
        }

        CheckUpdateButton.IsEnabled = false;
        try
        {
            UpdateStatusLine.Text = loc["Settings.Update.Checking"];
            var update = await AionDPS.Update.UpdateService.CheckAsync();
            if (update is null)
            {
                UpdateStatusLine.Text = loc["Settings.Update.UpToDate"];
                return;
            }

            string version = update.TargetFullRelease.Version.ToString();
            await AionDPS.Update.UpdateService.DownloadAsync(update, percent =>
                Dispatcher.BeginInvoke(() => UpdateStatusLine.Text = string.Format(loc["Settings.Update.Downloading"], version, percent)));
            UpdateStatusLine.Text = string.Format(loc["Settings.Update.Installing"], version);
            BeforeUpdateRestart?.Invoke();
            AionDPS.Update.UpdateService.ApplyAndRestart(update);
        }
        catch (Exception ex)
        {
            UpdateStatusLine.Text = string.Format(loc["Settings.Update.Failed"], ex.Message);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OnDigitsOnly(object sender, System.Windows.Input.TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private IEnumerable<TextBox> HotkeyBoxes() => new[] { HotkeyOverlayBox, HotkeyResetBox, HotkeyModeBox, HotkeyPauseBox };

    /// <summary>A shortcut box: the combination pressed replaces its text (it needs a modifier,
    /// so the game keeps its own keys); Backspace or Delete puts the default back.</summary>
    private void OnHotkeyBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        e.Handled = true;
        System.Windows.Input.Key key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var action = (MeterHotkey)Enum.Parse(typeof(MeterHotkey), (string)box.Tag);
        if (key is System.Windows.Input.Key.Back or System.Windows.Input.Key.Delete
            && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None)
        {
            box.Text = Hotkey.Defaults[action].ToString();
            return;
        }

        var hotkey = new Hotkey(System.Windows.Input.Keyboard.Modifiers, key);
        if (hotkey.IsUsable)
        {
            box.Text = hotkey.ToString();
        }
    }

    /// <summary>Two actions on one combination would leave one of them unreachable.</summary>
    private bool HotkeysAreDistinct()
    {
        var texts = HotkeyBoxes().Select(box => box.Text).ToList();
        if (texts.Distinct(StringComparer.OrdinalIgnoreCase).Count() == texts.Count)
        {
            return true;
        }

        MessageBox.Show(this, LocalizationManager.Instance["Settings.Hotkeys.Duplicate"], LocalizationManager.Instance["Settings.Hotkeys"],
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    public event Action? Saved;

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTitleBarMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>Saves size regardless of Save/Cancel/titlebar-✕/Alt-F4 - all of them end up here,
    /// same reasoning as MainWindow's own OnClosing/SaveWindowGeometry. Deliberately a FRESH
    /// MeterSettings.Load() rather than writing through _settings (this dialog's own working copy,
    /// which Cancel must NOT persist to disk) - only the size field changes, exactly like
    /// MainWindow's own geometry save stays decoupled from whatever else might be in flight.
    /// RestoreBounds (not Width/Height directly) so closing while minimized/maximized doesn't
    /// persist that transient state as if it were the normal size - CanResizeWithGrip allows
    /// maximizing too, same as MainWindow's own.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        MeterSettings onDisk = MeterSettings.Load();
        onDisk.SettingsWindowWidth = bounds.Width;
        onDisk.SettingsWindowHeight = bounds.Height;
        onDisk.Save();
        base.OnClosing(e);
    }
}
