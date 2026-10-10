using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>
/// The teasing lines shown at a boss's death (see <see cref="Combat.LowDpsTease"/>): a banner at
/// the top centre of the screen the meter is on, over the game, for a few seconds. Click-through and
/// never focused, like the overlay - it is a window of the meter on the local screen, nothing goes
/// to the game.
/// </summary>
internal sealed class TeaseBanner : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_LAYERED = 0x00080000, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private static readonly TimeSpan Shown = TimeSpan.FromSeconds(6);
    private static TeaseBanner? _current;
    private readonly IntPtr _meter;

    /// <summary>Shows the lines on the screen of <paramref name="meter"/>, replacing a banner still up.</summary>
    public static void ShowLines(IReadOnlyList<string> lines, Window meter)
    {
        _current?.Close();
        _current = new TeaseBanner(lines, new WindowInteropHelper(meter).Handle);
        _current.Show();
    }

    private TeaseBanner(IReadOnlyList<string> lines, IntPtr meter)
    {
        _meter = meter;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = BuildContent(lines);

        SourceInitialized += (_, _) =>
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
        Loaded += (_, _) => PlaceOnMeterScreen();
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        };

        var timer = new DispatcherTimer { Interval = Shown };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(600));
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }

    /// <summary>The banner's look, also rendered on its own by the replay tools.</summary>
    internal static FrameworkElement BuildContent(IReadOnlyList<string> lines)
    {
        var panel = new StackPanel();
        foreach (string line in lines)
        {
            panel.Children.Add(new TextBlock
            {
                Text = line,
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.9 },
            });
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x12, 0x10, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xE0, 0x5A, 0x5A)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 10, 22, 10),
            Child = panel,
        };
    }

    /// <summary>Top centre of the monitor the meter is on (where the game is).</summary>
    private void PlaceOnMeterScreen()
    {
        var screen = _meter != IntPtr.Zero
            ? System.Windows.Forms.Screen.FromHandle(_meter)
            : System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null)
        {
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double left = screen.Bounds.Left / dpi.DpiScaleX, top = screen.Bounds.Top / dpi.DpiScaleY;
        double width = screen.Bounds.Width / dpi.DpiScaleX, height = screen.Bounds.Height / dpi.DpiScaleY;
        Left = left + (width - ActualWidth) / 2;
        Top = top + height * 0.12;
        NativeOverlay.KeepOnTop(new WindowInteropHelper(this).Handle);
    }
}
