using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>The sprites of the Pokémon (assets/pokemon/normal and shiny, 96 x 96 with plenty of room round the drawing): cropped to the drawing, so a small Pokémon is not lost in its picture.</summary>
internal static class PokemonSprites
{
    private static readonly Dictionary<(int, bool), BitmapSource?> Cache = new();

    public static BitmapSource? Get(int id, bool shiny)
    {
        if (Cache.TryGetValue((id, shiny), out BitmapSource? cached))
        {
            return cached;
        }

        BitmapSource? result = null;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "pokemon", shiny ? "shiny" : "normal", id + ".png");
            if (File.Exists(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                int w = converted.PixelWidth, h = converted.PixelHeight, stride = w * 4;
                var pixels = new byte[stride * h];
                converted.CopyPixels(pixels, stride, 0);
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (pixels[y * stride + x * 4 + 3] > 20)
                        {
                            x0 = Math.Min(x0, x);
                            y0 = Math.Min(y0, y);
                            x1 = Math.Max(x1, x);
                            y1 = Math.Max(y1, y);
                        }
                    }
                }

                result = x1 < 0 ? converted : new CroppedBitmap(converted, new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
                result.Freeze();
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UriFormatException or ArgumentException)
        {
            result = null;
        }

        Cache[(id, shiny)] = result;
        return result;
    }

    public static string NameOf(DexEntry entry) =>
        string.Equals(LocalizationManager.Instance.Language, "fr", StringComparison.OrdinalIgnoreCase) ? entry.Fr : entry.En;

    public static Brush TierBrush(int tier) => new SolidColorBrush(tier switch
    {
        0 => Color.FromRgb(0xB8, 0xC4, 0xD6),
        1 => Color.FromRgb(0x6F, 0xE0, 0x8A),
        2 => Color.FromRgb(0x6F, 0xB8, 0xFF),
        3 => Color.FromRgb(0xC9, 0x8B, 0xFF),
        _ => Color.FromRgb(0xFF, 0xD2, 0x4A),
    });

    public static FontFamily Font() =>
        new(new Uri("file:///" + Path.Combine(AppContext.BaseDirectory, "assets", "fonts").Replace('\\', '/') + "/"), "./#VT323");
}

/// <summary>"Wild Pokémon of the day": four cards, a click catches one (a ball, a few rocks, "Caught!"). It never fails.</summary>
internal sealed class PokemonPicker : Window
{
    private readonly PokemonTeam _team;
    private readonly PokemonDex _dex;
    private readonly int _slot;
    private readonly Action<Offer> _caught;
    private readonly BitmapSource? _ball;
    private bool _busy;

    public PokemonPicker(PokemonTeam team, PokemonDex dex, int slot, int level, BitmapSource? ball, Action<Offer> caught)
    {
        _team = team;
        _dex = dex;
        _slot = slot;
        _caught = caught;
        _ball = ball;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var loc = LocalizationManager.Instance;
        var cards = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (Offer offer in team.Today(DateTime.Now))
        {
            cards.Children.Add(BuildCard(offer, level));
        }

        var close = new TextBlock { Text = "✕", Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xC8, 0xE2)), FontSize = 18, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        close.MouseLeftButtonUp += (_, _) => Close();
        var title = new TextBlock { Text = loc["Monsters.Pokemon.Title"], FontFamily = PokemonSprites.Font(), FontSize = 30, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x5A)) };
        var header = new Grid();
        header.Children.Add(title);
        header.Children.Add(close);
        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(header);
        content.Children.Add(cards);
        Content = new Border
        {
            Child = content, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(3), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xC8)),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x21, 0x3A)),
        };
        MouseLeftButtonDown += (s, e) =>
        {
            if (e.OriginalSource is not Image && e.GetPosition(this).Y < 50)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    private UIElement BuildCard(Offer offer, int level)
    {
        DexEntry entry = _dex.Get(offer.Id)!;
        bool already = _team.Slots.Any(m => m?.Path[0] == offer.Id);
        var sprite = new Image { Source = PokemonSprites.Get(offer.Id, offer.Shiny), Width = 120, Height = 120, Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);
        var ball = new Image { Source = _ball, Width = 64, Height = 64, Opacity = 0, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new TransformGroup { Children = { new ScaleTransform(0.4, 0.4), new RotateTransform(0) } } };
        RenderOptions.SetBitmapScalingMode(ball, BitmapScalingMode.NearestNeighbor);
        var stage = new Grid { Width = 130, Height = 130 };
        stage.Children.Add(sprite);
        stage.Children.Add(ball);
        string name = PokemonSprites.NameOf(entry) + (offer.Shiny ? " ✦" : "");
        var label = new TextBlock { Text = name, FontFamily = PokemonSprites.Font(), FontSize = 24, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 140 };
        var tier = new TextBlock { Text = LocalizationManager.Instance["Monsters.Pokemon.Tier" + entry.Tier], FontFamily = PokemonSprites.Font(), FontSize = 20, Foreground = PokemonSprites.TierBrush(entry.Tier), HorizontalAlignment = HorizontalAlignment.Center };
        var info = new StackPanel();
        info.Children.Add(stage);
        info.Children.Add(label);
        info.Children.Add(tier);
        var card = new Border
        {
            Child = info, Margin = new Thickness(5), Padding = new Thickness(8), CornerRadius = new CornerRadius(8), Width = 156,
            Background = new SolidColorBrush(offer.Shiny ? Color.FromRgb(0x3A, 0x30, 0x14) : Color.FromRgb(0x1E, 0x30, 0x52)), BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(offer.Shiny ? Color.FromRgb(0xFF, 0xD2, 0x4A) : Color.FromRgb(0x2E, 0x4A, 0x7C)),
            Cursor = already ? Cursors.Arrow : Cursors.Hand, Opacity = already ? 0.45 : 1,
        };
        if (!already)
        {
            card.MouseEnter += (_, _) => { if (!_busy) { card.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)); } };
            card.MouseLeave += (_, _) => card.BorderBrush = new SolidColorBrush(offer.Shiny ? Color.FromRgb(0xFF, 0xD2, 0x4A) : Color.FromRgb(0x2E, 0x4A, 0x7C));
            card.MouseLeftButtonUp += (_, _) => Catch(offer, card, sprite, ball, info, level);
        }

        return card;
    }

    private void Catch(Offer offer, Border card, Image sprite, Image ball, StackPanel info, int level)
    {
        if (_busy || !_team.Catch(_slot, offer, level, DateTime.Now))
        {
            return;
        }

        _busy = true;
        _caught(offer);
        var scale = (ScaleTransform)sprite.RenderTransform;
        var group = (TransformGroup)ball.RenderTransform;
        var ballScale = (ScaleTransform)group.Children[0];
        var rotate = (RotateTransform)group.Children[1];
        // the Pokémon goes into the ball
        var shrink = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.4)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        sprite.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.4)));
        ball.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)) { BeginTime = TimeSpan.FromSeconds(0.25) });
        var grow = new DoubleAnimation(0.4, 1, TimeSpan.FromSeconds(0.3)) { BeginTime = TimeSpan.FromSeconds(0.25), EasingFunction = new BackEase { Amplitude = 0.6 } };
        ballScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        ballScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        // three rocks
        var rock = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(0.8) };
        double t = 0;
        foreach (double a in new[] { -22, 22, -18, 18, -10, 10, 0 })
        {
            t += 0.17;
            rock.KeyFrames.Add(new EasingDoubleKeyFrame(a, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t)), new SineEase()));
        }

        rotate.BeginAnimation(RotateTransform.AngleProperty, rock);
        var done = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(0.8 + t + 0.15) };
        done.Tick += (_, _) =>
        {
            done.Stop();
            info.Children.Clear();
            info.Children.Add(new TextBlock
            {
                Text = LocalizationManager.Instance["Monsters.Pokemon.Caught"], FontFamily = PokemonSprites.Font(), FontSize = 40, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x5A)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 50, 0, 50),
            });
            var closeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.1) };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                Close();
            };
            closeTimer.Start();
        };
        done.Start();
    }
}

/// <summary>The team: one line per place with the Pokémon, its form and when it evolves next, and a button to let it go.</summary>
internal sealed class PokemonTeamWindow : Window
{
    private readonly PokemonTeam _team;
    private readonly PokemonDex _dex;
    private readonly int _level;
    private readonly Action _changed;
    private readonly StackPanel _rows = new();

    public PokemonTeamWindow(PokemonTeam team, PokemonDex dex, int level, Action changed)
    {
        _team = team;
        _dex = dex;
        _level = level;
        _changed = changed;
        Title = LocalizationManager.Instance["Monsters.Pokemon.TeamTitle"];
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x21, 0x3A));
        Content = new Border { Child = _rows, Padding = new Thickness(14) };
        Rebuild();
    }

    private void Rebuild()
    {
        _rows.Children.Clear();
        var loc = LocalizationManager.Instance;
        for (int i = 0; i < PokemonTeam.SlotLevels.Length; i++)
        {
            int slot = i;
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4), MinHeight = 64 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TeamMember? m = _team.Slots[i];
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (m is not null)
            {
                int stage = PokemonTeam.StageAt(m, _level);
                DexEntry entry = _dex.Get(m.Path[stage])!;
                var sprite = new Image { Source = PokemonSprites.Get(entry.Id, m.Shiny), Width = 60, Height = 60, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);
                row.Children.Add(sprite);
                text.Children.Add(new TextBlock { Text = PokemonSprites.NameOf(entry) + (m.Shiny ? " ✦" : ""), FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
                string next = m.Path.Length == 1 ? loc["Monsters.Pokemon.NoEvolution"] : stage + 1 < m.Path.Length
                    ? string.Format(CultureInfo.CurrentCulture, loc["Monsters.Pokemon.NextEvolution"], m.CaughtLevel + (stage + 1) * PokemonTeam.LevelsPerEvolution)
                    : loc["Monsters.Pokemon.FullyEvolved"];
                text.Children.Add(new TextBlock { Text = (m.Path.Length > 1 ? string.Format(CultureInfo.CurrentCulture, loc["Monsters.Pokemon.Form"], stage + 1, m.Path.Length) + " · " : "") + next, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xC8, 0xE2)) });
                var release = new Button { Content = loc["Monsters.Pokemon.Release"], Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 4, 10, 4) };
                release.Click += (_, _) =>
                {
                    string ask = string.Format(CultureInfo.CurrentCulture, loc["Monsters.Pokemon.ReleaseAsk"], PokemonSprites.NameOf(entry));
                    if (MessageBox.Show(this, ask, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        _team.Release(slot);
                        _changed();
                        Rebuild();
                    }
                };
                Grid.SetColumn(release, 2);
                row.Children.Add(release);
            }
            else
            {
                bool open = PokemonTeam.SlotsOpen(_level) > i;
                text.Children.Add(new TextBlock
                {
                    Text = open ? loc["Monsters.Pokemon.EmptyOpen"] : string.Format(CultureInfo.CurrentCulture, loc["Monsters.Pokemon.EmptyLocked"], PokemonTeam.SlotLevels[i]),
                    FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xC8, 0xE2)), TextWrapping = TextWrapping.Wrap,
                });
            }

            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            _rows.Children.Add(new Border { Child = row, Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x30, 0x52)), CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Margin = new Thickness(0, 3, 0, 3) });
        }
    }
}
