using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using AionDPS.Combat;

namespace AionDPS.Ui;

/// <summary>The Monsters console's team of Pokémon (Stacy, 2026-11-24): the four places of the console show the Pokémon caught (or a ball to click to catch one).</summary>
public partial class MainWindow
{
    private PokemonDex? _dex;
    private PokemonTeam? _pokemon;
    private readonly bool[] _slotFilled = new bool[4];

    private bool EnsurePokemon()
    {
        _dex ??= PokemonDex.Load();
        if (_dex is not null && _pokemon is null)
        {
            _pokemon = PokemonTeam.Load(_dex, Path.Combine(AppDataFolder.Path, "pokemon-team.json"));
        }

        return _dex is not null && _pokemon is not null;
    }

    /// <summary>Draws the team in the four places of the console (and tells of the evolutions, unless <paramref name="silent"/>).</summary>
    private void RefreshPokemonSlots(bool silent)
    {
        if (!_monstersSkinned || _monstersSkin is null || !EnsurePokemon() || _pokemon is null || _dex is null)
        {
            return;
        }

        int level = _xp.Level;
        List<(int Slot, int From, int To)> evolved;
        if (silent)
        {
            _pokemon.Sync(level);
            evolved = [];
        }
        else
        {
            evolved = _pokemon.TakeEvolutions(level);
        }

        BitmapSource? ball = _monstersSkin.Bitmap("boule.png");
        var creatures = Skin.SkinFrame.Creatures;
        int open = PokemonTeam.SlotsOpen(level);
        for (int i = 0; i < creatures.Count && i < 4; i++)
        {
            Skin.SkinFrame.Creature c = creatures[i];
            TeamMember? member = _pokemon.Slots[i];
            _slotFilled[i] = member is not null;
            if (Math.Abs(c.Holder.Width - 30) > 0.01 || Math.Abs(c.Holder.Height - 30) > 0.01)
            {
                // the place is made as big as the Pokémon: an image bigger than its box is cut to it (the old creatures were smaller), so the box grows - upwards and to both sides, the foot stays where it was
                double growW = 30 - c.Holder.Width, growH = 30 - c.Holder.Height;
                Thickness m = c.Holder.Margin;
                bool left = c.Holder.HorizontalAlignment == HorizontalAlignment.Left;
                c.Holder.Margin = new Thickness(left ? m.Left - growW / 2 : m.Left, m.Top - growH, left ? m.Right : m.Right - growW / 2, m.Bottom);
                c.Holder.Width = 30;
                c.Holder.Height = 30;
            }

            c.Image.Width = 30;
            c.Image.Height = 30;
            c.Image.HorizontalAlignment = HorizontalAlignment.Center;
            c.Image.VerticalAlignment = VerticalAlignment.Bottom;
            c.Image.IsHitTestVisible = true;
            c.Holder.IsHitTestVisible = true;
            c.Holder.Cursor = Cursors.Hand;
            c.Holder.Tag = i;
            c.Holder.MouseLeftButtonUp -= OnPokemonSlotClicked;
            c.Holder.MouseLeftButtonUp += OnPokemonSlotClicked;
            if (member is not null)
            {
                DexEntry entry = _dex.Get(PokemonTeam.IdAt(member, level))!;
                c.Image.Source = PokemonSprites.Get(entry.Id, member.Shiny) ?? c.Image.Source;
                RenderOptions.SetBitmapScalingMode(c.Image, BitmapScalingMode.HighQuality);
                c.Image.Opacity = 1;
                c.Image.Margin = new Thickness(0);
                c.Holder.ToolTip = PokemonSprites.NameOf(entry) + (member.Shiny ? " ✦" : "");
            }
            else
            {
                c.Image.Source = ball ?? c.Image.Source;
                c.Image.Width = 20;
                c.Image.Height = 20;
                c.Image.Margin = new Thickness(0, 0, 0, 3);
                RenderOptions.SetBitmapScalingMode(c.Image, BitmapScalingMode.NearestNeighbor);
                c.Image.Opacity = i < open ? 1 : 0.35;
                c.Holder.ToolTip = i < open
                    ? LocalizationManager.Instance["Monsters.Pokemon.CatchOne"]
                    : string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance["Monsters.Pokemon.LockedSlot"], PokemonTeam.SlotLevels[i]);
            }
        }

        // the host of the skin does not take clicks (frame, screen...): only the four places do
        WinterSkinHost.IsHitTestVisible = true;
        var places = creatures.Take(4).Select(c => (UIElement)c.Holder).ToHashSet();
        foreach (UIElement child in WinterSkinHost.Children)
        {
            if (!places.Contains(child))
            {
                child.IsHitTestVisible = false;
            }
        }

        foreach ((int slot, int from, int to) in evolved)
        {
            DexEntry? before = _dex.Get(from), after = _dex.Get(to);
            if (before is null || after is null)
            {
                continue;
            }

            string line = string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance["Monsters.Pokemon.Evolves"], PokemonSprites.NameOf(before), PokemonSprites.NameOf(after));
            ScreenAnnounce.Show(line, Color.FromRgb(0xA9, 0xE6, 0xFF), 56, 1, 3.2);
            if (slot < creatures.Count)
            {
                var halo = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.White, BlurRadius = 24, ShadowDepth = 0, Opacity = 1 };
                Image image = creatures[slot].Image;
                image.Effect = halo;
                halo.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(24, 0, TimeSpan.FromSeconds(1.6)));
                var off = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.7) };
                off.Tick += (_, _) =>
                {
                    off.Stop();
                    image.Effect = null;
                };
                off.Start();
            }
        }
    }

    private void OnPokemonSlotClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int slot } || !EnsurePokemon() || _pokemon is null || _dex is null)
        {
            return;
        }

        e.Handled = true;
        int level = _xp.Level;
        if (_pokemon.Slots[slot] is not null)
        {
            OpenPokemonTeam();
        }
        else if (slot < PokemonTeam.SlotsOpen(level))
        {
            var picker = new PokemonPicker(_pokemon, _dex, slot, level, _monstersSkin?.Bitmap("boule.png"), offer =>
            {
                if (_victorySound)
                {
                    MomentSound.Effect(offer.Shiny ? "shiny" : "level_up", _momentVolume);
                }

                RefreshPokemonSlots(true);
            });
            picker.Show();
        }
        else
        {
            ShowDexMessage(string.Format(CultureInfo.CurrentCulture, LocalizationManager.Instance["Monsters.Pokemon.LockedSlot"], PokemonTeam.SlotLevels[slot]));
        }
    }

    /// <summary>The team window (from a click on a Pokémon, and from the settings): look at the team, let one go.</summary>
    internal void OpenPokemonTeam()
    {
        if (!EnsurePokemon() || _pokemon is null || _dex is null)
        {
            return;
        }

        new PokemonTeamWindow(_pokemon, _dex, _xp.Level, () => RefreshPokemonSlots(true)).Show();
    }

    /// <summary>The place that shines in the shiny moment: one with a Pokémon in it when there is one.</summary>
    private int PickShinySlot()
    {
        int count = Skin.SkinFrame.Creatures.Count;
        var filled = Enumerable.Range(0, Math.Min(count, 4)).Where(i => _slotFilled[i]).ToList();
        return filled.Count > 0 ? filled[Random.Shared.Next(filled.Count)] : Random.Shared.Next(Math.Max(1, count));
    }
}
