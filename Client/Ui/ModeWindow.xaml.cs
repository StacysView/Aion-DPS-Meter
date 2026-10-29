using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace AionDPS.Ui;

/// <summary>
/// A small window for one meter mode (Soins, Degats recus; Settings > Overlay, optional - Stacy, 2026-10-29): the overlay's rows in a list of its own, computed by
/// MainWindow for the overlay's current tab (Tous / Groupe / Raid / PvP). Its place and size are kept in the settings.
/// </summary>
public partial class ModeWindow : Window
{
    public ObservableCollection<PlayerRow> Rows { get; } = new();

    public Dictionary<int, PlayerRow> ById { get; } = new();

    /// <summary>The player closed it with its cross: the settings box is unticked.</summary>
    public event Action? ClosedByUser;

    /// <summary>A click on a player's row: the skill breakdown of this window's kind (the healing, the damage taken).</summary>
    public event Action<PlayerRow>? RowClicked;

    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PlayerRow row)
        {
            RowClicked?.Invoke(row);
        }
    }

    /// <summary>Its place or size stopped changing (the player let go): the settings keep them.</summary>
    public event Action? BoundsSettled;

    private readonly System.Windows.Threading.DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(700) };

    public ModeWindow(string title, string rateHeader)
    {
        InitializeComponent();
        TitleText.Text = title;
        RateText.Text = rateHeader;
        RowsList.ItemsSource = Rows;
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(Rows);
        view.SortDescriptions.Add(new SortDescription(nameof(PlayerRow.Damage), ListSortDirection.Descending));
        view.IsLiveSorting = true;
        view.LiveSortingProperties.Add(nameof(PlayerRow.Damage));
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            BoundsSettled?.Invoke();
        };
        LocationChanged += (_, _) => { _settle.Stop(); _settle.Start(); };
        SizeChanged += (_, _) => { _settle.Stop(); _settle.Start(); };
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClicked(object sender, MouseButtonEventArgs e)
    {
        ClosedByUser?.Invoke();
        Close();
    }

    /// <summary>"left,top,width,height" as the settings keep it.</summary>
    public string BoundsText => string.Create(CultureInfo.InvariantCulture, $"{Left:0},{Top:0},{Width:0},{Height:0}");

    /// <summary>The saved place, only when it is still on the screen (a monitor unplugged since would hide the window for good).</summary>
    public static bool TryParseBounds(string? text, out Rect bounds)
    {
        bounds = default;
        string[] parts = (text ?? "").Split(',');
        if (parts.Length != 4 || !parts.All(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            return false;
        }

        double[] v = parts.Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        bounds = new Rect(v[0], v[1], v[2], v[3]);
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return v[2] >= 220 && v[3] >= 80 && v[2] <= 2000 && v[3] <= 2000 && screen.IntersectsWith(new Rect(v[0], v[1], 60, 30));
    }
}
