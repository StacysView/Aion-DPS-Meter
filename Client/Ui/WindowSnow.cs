using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AionDPS.Ui;

/// <summary>
/// The light Christmas look of a window (Stacy, 2026-10-29: "le léger c'est pour les settings avec neige animée"): a few small flakes fall slowly over the window, each at its
/// own pace with a slow sway, only while the Christmas theme is the one drawn (the seasonal look or picked by hand, see <see cref="ThemeManager.CurrentTheme"/>); the overlay
/// keeps its own full decor. The flakes sit above the content and never take a click.
/// </summary>
internal sealed class WindowSnow
{
    private readonly Window _window;
    private readonly Canvas _canvas = new() { IsHitTestVisible = false, ClipToBounds = true, Margin = new Thickness(6) };
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(300) };

    private WindowSnow(Window window)
    {
        _window = window;
        UIElement? original = window.Content as UIElement;
        window.Content = null;
        var host = new Grid();
        if (original is not null)
        {
            host.Children.Add(original);
        }

        host.Children.Add(_canvas);
        window.Content = host;
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Rebuild();
        };
        host.SizeChanged += (_, _) =>
        {
            _settle.Stop();
            _settle.Start();
        };
        ThemeManager.Changed += OnThemeChanged;
        window.Closed += (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged;
            _settle.Stop();
            Clear();
        };
        Rebuild();
    }

    /// <summary>Makes the window's snow: it falls whenever the Christmas theme is on.</summary>
    public static void Attach(Window window) => _ = new WindowSnow(window);

    /// <summary>How many flakes fall in this window now (the tests).</summary>
    internal static int FlakesIn(Window window) => (window.Content as Grid)?.Children.OfType<Canvas>().LastOrDefault()?.Children.Count ?? 0;

    private void OnThemeChanged() => _window.Dispatcher.BeginInvoke(Rebuild);

    private void Clear()
    {
        foreach (UIElement child in _canvas.Children)
        {
            if (child is Ellipse { RenderTransform: TranslateTransform move })
            {
                move.BeginAnimation(TranslateTransform.YProperty, null);
                move.BeginAnimation(TranslateTransform.XProperty, null);
            }
        }

        _canvas.Children.Clear();
    }

    private void Rebuild()
    {
        Clear();
        if (ThemeManager.CurrentTheme != "Christmas")
        {
            return;
        }

        double width = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
        double height = _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 120 || height < 120)
        {
            return;
        }

        width -= 12;
        height -= 12;
        var random = new Random(11);
        int count = (int)Math.Clamp(width * height / 14000, 18, 60);
        var sizes = new[] { 1.8, 2.4, 3.0, 3.6 };
        var white = new SolidColorBrush(Color.FromRgb(0xF4, 0xF8, 0xFF));
        white.Freeze();
        for (int i = 0; i < count; i++)
        {
            var move = new TranslateTransform(0, -8);
            double size = sizes[random.Next(sizes.Length)];
            var flake = new Ellipse { Width = size, Height = size, Fill = white, Opacity = 0.25 + random.NextDouble() * 0.35, RenderTransform = move };
            Canvas.SetLeft(flake, 4 + random.NextDouble() * Math.Max(1, width - 8));
            _canvas.Children.Add(flake);
            double seconds = 8 + random.NextDouble() * 9;
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, height + 4, TimeSpan.FromSeconds(seconds))
            {
                RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(random.NextDouble() * seconds),
            });
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-6, 6, TimeSpan.FromSeconds(3 + random.NextDouble() * 3))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }
    }
}
