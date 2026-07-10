namespace SpeedTest.Core;

/// <summary>Misst die Upload-Geschwindigkeit über mehrere parallele HTTP-Streams.</summary>
public class UploadMeter
{
    private const string TestUrl = "https://speed.cloudflare.com/__up";
    private const int StreamCount = 3;
    private const long BytesPerRequest = 25 * 1024 * 1024;

    /// <summary>
    /// Lädt 10 Sekunden lang mit 3 parallelen Streams zum Testserver hoch; die ersten 2 Sekunden
    /// (Warm-up) fließen nicht ins Ergebnis ein. Neben der Geschwindigkeit wird die Zahl
    /// fehlgeschlagener Transfers geliefert (0-Werte wegen Serverfehlern erkennbar).
    /// <paramref name="liveSpeed"/> erhält alle 200 ms die aktuelle Geschwindigkeit in Mbit/s.
    /// </summary>
    public async Task<ThroughputResult> MeasureAsync(IProgress<double>? liveSpeed = null, CancellationToken ct = default)
    {
        var session = new ThroughputSession();
        return await session.RunAsync(
            StreamCount,
            token => UploadOnceAsync(session, token),
            liveSpeed,
            ct);
    }

    private static async Task UploadOnceAsync(ThroughputSession session, CancellationToken token)
    {
        using var content = new RandomDataContent(BytesPerRequest, session.AddBytes);
        using var response = await SpeedTestHttp.Client.PostAsync(TestUrl, content, token);
        response.EnsureSuccessStatusCode();
    }
}
