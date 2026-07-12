namespace SpeedTest.Core;

/// <summary>Misst die Download-Geschwindigkeit über mehrere parallele HTTP-Streams.</summary>
public class DownloadMeter
{
    // Cloudflare lehnt bytes-Werte ab ~100 MB inzwischen mit 403 ab; 50 MB pro Request
    // sind erlaubt, und die Transfer-Schleifen starten ohnehin laufend neue Requests.
    private const string TestUrl = "https://speed.cloudflare.com/__down?bytes=50000000";
    private const int StreamCount = 4;
    private const int BlockSize = 80 * 1024;

    /// <summary>Feste Gesamtdauer der Messung; die GUI leitet daraus ihren Fortschrittsbalken ab.</summary>
    public static readonly TimeSpan TestDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Lädt 10 Sekunden lang mit 4 parallelen Streams vom Testserver; die ersten 2 Sekunden
    /// (Warm-up) fließen nicht ins Ergebnis ein. Neben der Geschwindigkeit wird die Zahl
    /// fehlgeschlagener Transfers geliefert (0-Werte wegen Serverfehlern erkennbar).
    /// <paramref name="liveSpeed"/> erhält alle 200 ms die aktuelle Geschwindigkeit in Mbit/s
    /// (gleitender 1-s-Durchschnitt).
    /// </summary>
    public async Task<ThroughputResult> MeasureAsync(IProgress<double>? liveSpeed = null, CancellationToken ct = default)
    {
        var session = new ThroughputSession(TestDuration);
        return await session.RunAsync(
            StreamCount,
            token => DownloadOnceAsync(session, token),
            liveSpeed,
            ct);
    }

    private static async Task DownloadOnceAsync(ThroughputSession session, CancellationToken token)
    {
        // ResponseHeadersRead: Der Body wird gestreamt statt gepuffert — bei 128 MiB Pflicht.
        using var response = await SpeedTestHttp.Client.GetAsync(
            TestUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[BlockSize];

        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
            session.AddBytes(read);
    }
}
