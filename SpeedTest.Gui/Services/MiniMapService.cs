using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpeedTest.Gui.Services;

/// <summary>
/// Lädt kleine Kartenausschnitte um einen Punkt aus OpenStreetMap-Tiles (Zoom 11)
/// und setzt sie zu einem zentrierten Bild zusammen. Ergebnisse werden im Speicher
/// gecacht; Fehler liefern null — die Karte ist reiner Komfort.
/// </summary>
public sealed class MiniMapService
{
    private const int Zoom = 11;
    private const int TileSize = 256;

    private static readonly HttpClient Http = CreateClient();

    private readonly Dictionary<string, ImageSource> _cache = [];

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        // Die OSM-Tile-Policy verlangt einen identifizierenden User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SpeedTest-Gui/1.0");
        return client;
    }

    public async Task<ImageSource?> GetMapAsync(double latitude, double longitude, int width, int height)
    {
        var key = FormattableString.Invariant($"{latitude},{longitude},{width}x{height}");
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var map = await RenderAsync(latitude, longitude, width, height);
            _cache[key] = map;
            return map;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<ImageSource> RenderAsync(double latitude, double longitude, int width, int height)
    {
        // Web-Mercator: Weltpixel-Koordinaten des Zielpunkts bei diesem Zoom
        var worldTiles = Math.Pow(2, Zoom);
        var latRad = latitude * Math.PI / 180;
        var xWorld = (longitude + 180) / 360 * worldTiles * TileSize;
        var yWorld = (1 - Math.Log(Math.Tan(latRad) + 1 / Math.Cos(latRad)) / Math.PI) / 2 * worldTiles * TileSize;

        // Ausschnitt so wählen, dass der Punkt in der Mitte liegt
        var left = xWorld - width / 2.0;
        var top = yWorld - height / 2.0;

        var firstTileX = (int)Math.Floor(left / TileSize);
        var firstTileY = (int)Math.Floor(top / TileSize);
        var lastTileX = (int)Math.Floor((left + width - 1) / TileSize);
        var lastTileY = (int)Math.Floor((top + height - 1) / TileSize);

        var tiles = new List<(int X, int Y, Task<byte[]> Data)>();
        for (var tx = firstTileX; tx <= lastTileX; tx++)
        {
            for (var ty = firstTileY; ty <= lastTileY; ty++)
                tiles.Add((tx, ty, Http.GetByteArrayAsync($"https://tile.openstreetmap.org/{Zoom}/{tx}/{ty}.png")));
        }

        // Erst alle Tiles vollständig laden, dann in einem Stück zeichnen: WPF-Objekte
        // sind thread-affin, ein await mitten im Zeichnen würde sie über Threads verteilen.
        await Task.WhenAll(tiles.Select(t => t.Data));

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            foreach (var (tx, ty, dataTask) in tiles)
            {
                using var stream = new MemoryStream(dataTask.Result);
                var tile = new BitmapImage();
                tile.BeginInit();
                tile.CacheOption = BitmapCacheOption.OnLoad;
                tile.StreamSource = stream;
                tile.EndInit();
                tile.Freeze();
                context.DrawImage(tile, new Rect(tx * TileSize - left, ty * TileSize - top, TileSize, TileSize));
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
