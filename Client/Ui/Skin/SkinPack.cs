using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AionDPS.Ui.Skin;

/// <summary>One image of a skin: its file and its default size in points (the 320-point-wide panel's); for a row background the width of its rounded ends.</summary>
public sealed record SkinImage(string File, double W, double H, double CapLeft = 0, double CapRight = 0);

/// <summary>The frame of a skin, cut in 9 by skin-pack: the pieces' files, the points per pixel, how far the ornaments overhang the frame's outer edge, the corner sizes.</summary>
public sealed record SkinFrameInfo(double Ppp, Thickness Overhang, Size Corner, IReadOnlyDictionary<string, string> Pieces, double Band)
{
    /// <summary>The lights of the frame that flicker (the lanterns of the Noël skin): in which corner, their centre in points from that corner's top-left, their size.</summary>
    public IReadOnlyList<SkinGlow> Glows { get; init; } = Array.Empty<SkinGlow>();

    /// <summary>The left and right pieces are repeated down the sides instead of stretched (skin.json frame "tileSides").</summary>
    public bool TileSides { get; init; }

    /// <summary>Layers of the frame's little lights that blink (Stacy, 2026-11-02): the frame's own pieces carry the bulbs dimmed, each layer has the same pieces with a group of bulbs lit.</summary>
    public IReadOnlyList<SkinLightLayer> Lights { get; init; } = Array.Empty<SkinLightLayer>();
}

/// <summary>
/// The strings of little lights drawn along the frame (skin.json "garland"): the bulb's size and the space between two bulbs in points, the seconds of a cycle (one group lit, then the other),
/// the layout - "outside" (hung all around the outside of the frame) or "partial" (a few swags inside, the snow left bare in between) - and how far in points the lights go out of the
/// frame's outer edge (the room the window must give them).
/// </summary>
public sealed record SkinGarlandInfo(double Bulb, double Spacing, double Period, string Layout, double Reach);

/// <summary>A group of bulbs of the frame: the pieces' files (the same names as the frame's) with only these bulbs lit, and the seconds a blink takes.</summary>
public sealed record SkinLightLayer(IReadOnlyDictionary<string, string> Pieces, double Period);

/// <summary>
/// A decoration outside the frame (Stacy, 2026-10-30: "quelques elements a l'exterieur, hors cadre mais leger"): its file and size in points, the corner of the panel it belongs to
/// (Anchor: tl, tr, bl, br), how far it is moved along the edge from that corner (Along), how far it goes out of the panel sideways (Out) and up or down (Down), a slow sway in
/// degrees around a pivot (fractions of the image), and an optional flickering light (centre and size in fractions of the image).
/// </summary>
public sealed record SkinDeco(string File, double W, double H, string Anchor, double Along, double Out, double Down, double Sway, Point Pivot, Point? Light, double LightSize, bool Twinkle);

/// <summary>A flickering light of a frame: a corner piece (tl, tr, bl, br), the centre in points from its top-left, the halo's size in points.</summary>
public sealed record SkinGlow(string Corner, double X, double Y, double Size);

/// <summary>
/// A skin (Stacy, 2026-10-30): the look of one theme, drawn by Gemini as images and shown as they are - assets/skins/&lt;id&gt;/ with a skin.json and PNG files made by
/// Tools/skin-pack (format in Tools/skin-pack/SPEC.md). Loading never throws: a missing or broken skin is null and the meter keeps what it draws itself.
/// </summary>
public sealed class SkinPack
{
    private static readonly Dictionary<string, SkinPack?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource?> _bitmaps = new(StringComparer.OrdinalIgnoreCase);

    public string Id { get; private init; } = "";

    public string Name { get; private init; } = "";

    /// <summary>The theme (ThemeManager) this skin dresses.</summary>
    public string Theme { get; private init; } = "";

    public string Folder { get; private init; } = "";

    public SkinFrameInfo? Frame { get; private init; }

    /// <summary>The named single images (boule, etincelle, lueur_dps, filet_or, ligne_joueur, bouton_degats, info, onglet_ferme, onglet_ouvert...).</summary>
    public IReadOnlyDictionary<string, SkinImage> Images { get; private init; } = new Dictionary<string, SkinImage>();

    /// <summary>The snowflakes of the DPS bar, left to right.</summary>
    public IReadOnlyList<SkinImage> Flakes { get; private init; } = Array.Empty<SkinImage>();

    /// <summary>The strings of lights along the frame, drawn as vectors (skin.json "garland"), or null.</summary>
    public SkinGarlandInfo? Garland { get; private init; }

    /// <summary>The decorations outside the frame (skin.json "decos"), those whose file is there.</summary>
    public IReadOnlyList<SkinDeco> Decos { get; private init; } = Array.Empty<SkinDeco>();

    /// <summary>The skin that dresses a theme (Stacy, 2026-10-30), if the theme has one: Winter is "hiver".</summary>
    public static string? IdFor(string theme) => theme switch { "Winter" => "hiver", "Christmas" => "noel", "NewYear" => "nouvel-an", _ => null };

    /// <summary>The skin of this id in assets/skins, or null (no folder, no skin.json, a broken one). Cached.</summary>
    public static SkinPack? Load(string id)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(id, out SkinPack? skin))
            {
                Cache[id] = skin = LoadFrom(Path.Combine(AppContext.BaseDirectory, "assets", "skins", id));
            }

            return skin;
        }
    }

    /// <summary>Forgets the cached skins (the tests, a skin folder changed while the meter runs).</summary>
    public static void ClearCache()
    {
        lock (Cache)
        {
            Cache.Clear();
        }
    }

    public static SkinPack? LoadFrom(string folder)
    {
        try
        {
            string path = Path.Combine(folder, "skin.json");
            if (!File.Exists(path))
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("version", out JsonElement version) && version.GetInt32() != 1)
            {
                return null;
            }

            SkinFrameInfo? frame = null;
            if (root.TryGetProperty("frame", out JsonElement f))
            {
                JsonElement o = f.GetProperty("overhang");
                JsonElement c = f.GetProperty("corner");
                var pieces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty piece in f.GetProperty("pieces").EnumerateObject())
                {
                    pieces[piece.Name] = piece.Value.GetString() ?? "";
                }

                string[] needed = { "tl", "t", "tr", "l", "r", "bl", "b", "br" };
                if (needed.All(n => pieces.TryGetValue(n, out string? file) && File.Exists(Path.Combine(folder, file))))
                {
                    frame = new SkinFrameInfo(f.GetProperty("ppp").GetDouble(),
                        new Thickness(o.GetProperty("left").GetDouble(), o.GetProperty("top").GetDouble(), o.GetProperty("right").GetDouble(), o.GetProperty("bottom").GetDouble()),
                        new Size(c.GetProperty("w").GetDouble(), c.GetProperty("h").GetDouble()), pieces,
                        f.TryGetProperty("band", out JsonElement band) ? band.GetDouble() : 1.5)
                    {
                        Glows = f.TryGetProperty("glows", out JsonElement glows) && glows.ValueKind == JsonValueKind.Array
                            ? glows.EnumerateArray().Select(g => new SkinGlow(g.GetProperty("corner").GetString() ?? "", g.GetProperty("x").GetDouble(), g.GetProperty("y").GetDouble(),
                                g.GetProperty("size").GetDouble())).ToList()
                            : Array.Empty<SkinGlow>(),
                        TileSides = f.TryGetProperty("tileSides", out JsonElement tile) && tile.ValueKind == JsonValueKind.True,
                        Lights = f.TryGetProperty("lights", out JsonElement lights) && lights.ValueKind == JsonValueKind.Array
                            ? lights.EnumerateArray().Select(l => new SkinLightLayer(
                                l.GetProperty("pieces").EnumerateObject().ToDictionary(pr => pr.Name, pr => pr.Value.GetString() ?? "", StringComparer.OrdinalIgnoreCase),
                                l.TryGetProperty("period", out JsonElement period) ? period.GetDouble() : 1.5))
                                .Where(l => needed.All(n => l.Pieces.TryGetValue(n, out string? lf) && File.Exists(Path.Combine(folder, lf)))).ToList()
                            : Array.Empty<SkinLightLayer>(),
                    };
                }
            }

            var images = new Dictionary<string, SkinImage>(StringComparer.OrdinalIgnoreCase);
            var flakes = new List<SkinImage>();
            if (root.TryGetProperty("images", out JsonElement imgs))
            {
                foreach (JsonProperty entry in imgs.EnumerateObject())
                {
                    if (entry.Value.ValueKind == JsonValueKind.Array)
                    {
                        if (entry.Name.Equals("flocons", StringComparison.OrdinalIgnoreCase))
                        {
                            flakes.AddRange(entry.Value.EnumerateArray().Select(e => Image(e, folder)).OfType<SkinImage>());
                        }
                    }
                    else if (Image(entry.Value, folder) is SkinImage image)
                    {
                        images[entry.Name] = image;
                    }
                }
            }

            var decos = new List<SkinDeco>();
            if (root.TryGetProperty("decos", out JsonElement ds) && ds.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement d in ds.EnumerateArray())
                {
                    string file = d.GetProperty("file").GetString() ?? "";
                    if (!File.Exists(Path.Combine(folder, file)))
                    {
                        continue;
                    }

                    double Num(string key, double fallback) => d.TryGetProperty(key, out JsonElement v) ? v.GetDouble() : fallback;
                    Point? light = d.TryGetProperty("light", out JsonElement l) ? new Point(l.GetProperty("x").GetDouble(), l.GetProperty("y").GetDouble()) : null;
                    double lightSize = light is not null && l.TryGetProperty("size", out JsonElement ls) ? ls.GetDouble() : 0.4;
                    decos.Add(new SkinDeco(file, Num("w", 30), Num("h", 30), d.TryGetProperty("anchor", out JsonElement a) ? a.GetString() ?? "br" : "br", Num("along", 0),
                        Num("out", 0), Num("down", 0), Num("sway", 0), new Point(Num("pivotX", 0.5), Num("pivotY", 0)), light, lightSize,
                        d.TryGetProperty("twinkle", out JsonElement tw) && tw.ValueKind == JsonValueKind.True));
                }
            }

            SkinGarlandInfo? garland = root.TryGetProperty("garland", out JsonElement gl) && gl.ValueKind == JsonValueKind.Object
                ? new SkinGarlandInfo(gl.GetProperty("bulb").GetDouble(), gl.GetProperty("spacing").GetDouble(), gl.TryGetProperty("period", out JsonElement gp) ? gp.GetDouble() : 1.2,
                    gl.TryGetProperty("layout", out JsonElement gla) ? gla.GetString() ?? "outside" : "outside", gl.TryGetProperty("reach", out JsonElement gre) ? gre.GetDouble() : 0)
                : null;
            return new SkinPack
            {
                Garland = garland,
                Decos = decos,
                Id = root.GetProperty("id").GetString() ?? "", Name = root.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? "" : "",
                Theme = root.TryGetProperty("theme", out JsonElement theme) ? theme.GetString() ?? "" : "", Folder = folder, Frame = frame, Images = images, Flakes = flakes,
            };
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static SkinImage? Image(JsonElement e, string folder)
    {
        string? file = e.GetProperty("file").GetString();
        if (string.IsNullOrEmpty(file) || !File.Exists(Path.Combine(folder, file)))
        {
            return null;
        }

        return new SkinImage(file, e.GetProperty("w").GetDouble(), e.GetProperty("h").GetDouble(),
            e.TryGetProperty("capLeft", out JsonElement l) ? l.GetDouble() : 0, e.TryGetProperty("capRight", out JsonElement r) ? r.GetDouble() : 0);
    }

    /// <summary>A file of the skin as a frozen bitmap (decoded once), or null when it cannot be read.</summary>
    public BitmapSource? Bitmap(string file)
    {
        lock (_bitmaps)
        {
            if (!_bitmaps.TryGetValue(file, out BitmapSource? bitmap))
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(Path.Combine(Folder, file));
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    bitmap = image;
                }
                catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
                {
                    bitmap = null;
                }

                _bitmaps[file] = bitmap;
            }

            return bitmap;
        }
    }

    /// <summary>A named single image and its bitmap, or null.</summary>
    public (SkinImage Info, BitmapSource Bitmap)? Get(string name) =>
        Images.TryGetValue(name, out SkinImage? info) && Bitmap(info.File) is BitmapSource bitmap ? (info, bitmap) : null;
}
