using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// One world map made of <see cref="Grid"/> x <see cref="Grid"/> square tiles of <see cref="TilePixels"/> px
/// (assets/aion2/maps/&lt;Key&gt;/&lt;col&gt;_&lt;row&gt;.jpg, col = x, row = y, 0 at the top left). The map covers the
/// level bounds, <see cref="SizeMetres"/> across, centred on the world origin.
/// </summary>
public sealed record Aion2WorldMap(string Key, string SpawnTable, int Grid, int TilePixels, double SizeMetres, double MetresPerPixel)
{
    public int MapPixels => Grid * TilePixels;

    /// <summary>World position (centimetres, as sent by the game) to pixel on the whole map, no flip.</summary>
    public (double X, double Y) ToPixel(float worldX, float worldY) =>
        ((worldX / 100.0 + SizeMetres / 2) / MetresPerPixel, (worldY / 100.0 + SizeMetres / 2) / MetresPerPixel);

    /// <summary>Path of a tile relative to the application folder.</summary>
    public string TilePath(int col, int row) => $"assets/aion2/maps/{Key}/{col}_{row}.jpg";
}

/// <summary>
/// The three Elyos-side world maps (Verteron, Eltnen, starter zone), from assets/aion2/maps/maps.json. Calibration:
/// pixel = (world cm / 100 + size / 2) / metres per pixel, on both axes (World_L_A is confirmed on captures; the
/// other two come from the level bounds). Loaded on first use; a missing or broken file yields an empty list.
/// </summary>
public static class Aion2WorldMaps
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "maps", "maps.json");
    private static IReadOnlyList<Aion2WorldMap>? _cache;

    public static IReadOnlyList<Aion2WorldMap> All => _cache ??= Load();

    public static Aion2WorldMap? Get(string key) => All.FirstOrDefault(m => m.Key == key);

    /// <summary>The map of a zone id (1010/902 Verteron, 1011/904 Eltnen, 100001/100002 starter zone); null for any other zone.</summary>
    public static Aion2WorldMap? OfZone(int zoneId) => zoneId switch
    {
        1010 or 902 => Get("World_L_A"),
        1011 or 904 => Get("World_L_B"),
        100001 or 100002 => Get("World_L_Starter"),
        _ => null,
    };

    private static IReadOnlyList<Aion2WorldMap> Load()
    {
        var maps = new List<Aion2WorldMap>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(FilePath));
            foreach (JsonProperty entry in document.RootElement.GetProperty("maps").EnumerateObject())
            {
                int grid = entry.Value.GetProperty("grid").GetInt32();
                int tile = entry.Value.GetProperty("tile").GetInt32();
                double size = entry.Value.GetProperty("size").GetDouble();
                if (grid <= 0 || tile <= 0 || size <= 0)
                {
                    continue;
                }

                maps.Add(new Aion2WorldMap(entry.Name, entry.Value.GetProperty("spawnTable").GetString() ?? "", grid, tile, size, size / (grid * tile)));
            }
        }
        catch (Exception)
        {
            maps.Clear();
        }

        return maps;
    }
}
