using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace SpeedTest.Gui.Services;

/// <summary>Ein verfügbares Update: Zielversion, Download-URL und Dateiname des MSI-Assets.</summary>
public sealed record UpdateInfo(Version Version, string MsiUrl, string FileName);

/// <summary>
/// Prüft das neueste GitHub-Release auf eine neuere Version, lädt deren MSI herunter
/// und startet die Installation. Der Check ist strikt "best effort": offline, Rate-Limit,
/// kein Release, kein MSI-Asset — alles führt zu "kein Update", nie zu einem Fehler,
/// denn der Updater darf die App nicht stören.
/// </summary>
public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/keco216/SpeedTest/releases/latest";

    // Kein Client-Timeout: Er würde auch den laufenden MSI-Download (~50 MB) abschneiden;
    // der Check begrenzt sich stattdessen selbst über ein CancellationTokenSource.
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // Die GitHub-API lehnt Anfragen ohne User-Agent mit 403 ab.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SpeedTest", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>Version der laufenden App, auf drei Stellen normalisiert (wie die Release-Tags).</summary>
    private static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>
    /// Liefert das neueste GitHub-Release, wenn es neuer als die laufende Version ist
    /// und ein MSI-Asset mitbringt; sonst — auch bei jedem Fehler — null.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await Client.GetAsync(LatestReleaseUrl, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return null;

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = json.RootElement;

            if (!root.TryGetProperty("tag_name", out var tag) ||
                !Version.TryParse((tag.GetString() ?? "").TrimStart('v', 'V'), out var version))
                return null;

            var release = new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
            if (release <= CurrentVersion || !root.TryGetProperty("assets", out var assets))
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name) &&
                    name.GetString() is { } fileName &&
                    fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) &&
                    asset.TryGetProperty("browser_download_url", out var url) &&
                    url.GetString() is { } msiUrl)
                {
                    return new UpdateInfo(release, msiUrl, fileName);
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Lädt das MSI in den Temp-Ordner und liefert dessen Pfad;
    /// <paramref name="progress"/> erhält den Fortschritt als Anteil von 0 bis 1.
    /// </summary>
    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress = null)
    {
        var targetPath = Path.Combine(Path.GetTempPath(), update.FileName);

        using var response = await Client.GetAsync(update.MsiUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(targetPath);

        var buffer = new byte[80 * 1024];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read));
            written += read;
            if (total > 0)
                progress?.Report((double)written / total.Value);
        }

        return targetPath;
    }

    /// <summary>
    /// Startet die Installation entkoppelt von der App; der Aufrufer beendet sie direkt
    /// danach. Die Befehlskette wartet kurz (App-Ende, damit der Installer keine Dateien
    /// in Benutzung vorfindet), installiert per msiexec — /passive zeigt nur einen
    /// Fortschrittsbalken, die UAC-Abfrage kommt von Windows — und startet die App
    /// anschließend neu; auch nach einem Abbruch, dann eben in der alten Version.
    /// </summary>
    public void BeginInstall(string msiPath)
    {
        var exePath = Environment.ProcessPath!;
        var arguments =
            $"/c \"ping -n 3 127.0.0.1 >nul & msiexec /i \"{msiPath}\" /passive & " +
            $"start \"\" \"{exePath}\" & del /q \"{msiPath}\"\"";

        Process.Start(new ProcessStartInfo("cmd.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }
}
