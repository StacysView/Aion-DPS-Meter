using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace AionDPS.Ui;

/// <summary>
/// A line of big pixel lettering in the middle of the screen, over the game, with no panel behind it (Stacy, 2026-11-23: "un Salamèche sauvage apparaît, au milieu de l'écran, juste écrit"):
/// an outline keeps it readable on any background. It grows in, stays, fades; click-through and never takes the keyboard. One at a time per line (<c>row</c> 0 is a little above the middle,
/// 1 a little below).
/// </summary>
internal static class ScreenAnnounce
{
    private const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private static readonly Window?[] Windows = new Window?[2];

    /// <summary>Shows <paramref name="text"/> (replacing the one of that row); <paramref name="seconds"/> is how long it stays before fading.</summary>
    public static void Show(string text, Color fill, double size, int row = 0, double seconds = 2.4)
    {
        try
        {
            Windows[row]?.Close();
            Grid holder = BuildVisual(text, fill, size);
            var scale = new ScaleTransform(0.5, 0.5);
            holder.RenderTransform = scale;
            var window = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false, Focusable = false,
                ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight, Content = holder, IsHitTestVisible = false,
            };
            window.SourceInitialized += (_, _) =>
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            };
            Rect area = SystemParameters.WorkArea;
            window.Loaded += (_, _) =>
            {
                window.Left = area.Left + (area.Width - window.ActualWidth) / 2;
                window.Top = area.Top + area.Height * (row == 0 ? 0.34 : 0.34) + (row == 0 ? 0 : size * 1.25) - window.ActualHeight / 2;
            };
            Windows[row] = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(Windows[row], window))
                {
                    Windows[row] = null;
                }
            };
            window.Show();
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.5, 1, TimeSpan.FromSeconds(0.28)) { EasingFunction = new BackEase { Amplitude = 0.7 } });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.5, 1, TimeSpan.FromSeconds(0.28)) { EasingFunction = new BackEase { Amplitude = 0.7 } });
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, TimeSpan.Zero));
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, TimeSpan.FromSeconds(seconds)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(seconds + 0.6)));
            fade.Completed += (_, _) => window.Close();
            window.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UriFormatException)
        {
            // an announcement that cannot be drawn is not worth stopping the meter
        }
    }

    /// <summary>The lettering itself: the text as an outlined shape in a grid (also drawn alone by the previews).</summary>
    internal static Grid BuildVisual(string text, Color fill, double size)
    {
        FontFamily font = new FontFamily(new Uri("file:///" + System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "fonts").Replace('\\', '/') + "/"), "./#VT323");
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Brushes.White, 1.0);
        Geometry outline = formatted.BuildGeometry(new Point(0, 0));
        // the outline first, behind, then the colour on top of it: a stroke alone would cover the thin letters of the pixel font
        var rim = new Path { Data = outline, Stroke = Brushes.Black, StrokeThickness = Math.Max(4, size / 7), StrokeLineJoin = PenLineJoin.Round };
        var letters = new Path { Data = outline, Fill = new SolidColorBrush(fill), Stroke = new SolidColorBrush(fill), StrokeThickness = Math.Max(0.8, size / 40), StrokeLineJoin = PenLineJoin.Round };
        var stack = new Grid();
        stack.Children.Add(rim);
        stack.Children.Add(letters);
        double pad = Math.Max(4, size / 7);
        var holder = new Grid { Width = formatted.Width + pad * 2 + 8, Height = formatted.Height + pad * 2 + 8, RenderTransformOrigin = new Point(0.5, 0.5) };
        holder.Children.Add(new Viewbox { Child = stack, Margin = new Thickness(pad + 4) });
        return holder;
    }

    /// <summary>Closes every line shown (the theme was left).</summary>
    public static void Clear()
    {
        for (int i = 0; i < Windows.Length; i++)
        {
            try
            {
                Windows[i]?.Close();
            }
            catch (InvalidOperationException)
            {
            }

            Windows[i] = null;
        }
    }
}
