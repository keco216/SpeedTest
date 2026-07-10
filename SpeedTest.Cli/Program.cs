using SpeedTest.Core;

const string host = "1.1.1.1";
const int samples = 5;

Console.WriteLine($"Messe {samples} Pings gegen {host} ...");

var ping = await new PingMeter().MeasureAsync(host, samples);
Console.WriteLine($"Ping: {ping.AverageMs:0} ms | Jitter: {ping.JitterMs:0.#} ms | Verlust: {ping.PacketLossPercent:0.#} %");

var download = await MeasureWithLiveLineAsync("Download", new DownloadMeter().MeasureAsync);
var upload = await MeasureWithLiveLineAsync("Upload", new UploadMeter().MeasureAsync);

Console.WriteLine();
Console.WriteLine("Zusammenfassung");
Console.WriteLine($"  Ping:     {ping.AverageMs,6:0} ms");
Console.WriteLine($"  Jitter:   {ping.JitterMs,6:0.#} ms");
Console.WriteLine($"  Verlust:  {ping.PacketLossPercent,6:0.#} %");
Console.WriteLine($"  Download: {download,6:0.0} Mbit/s");
Console.WriteLine($"  Upload:   {upload,6:0.0} Mbit/s");

static async Task<double> MeasureWithLiveLineAsync(
    string label, Func<IProgress<double>?, CancellationToken, Task<ThroughputResult>> measure)
{
    Console.Write($"{label}: Messung läuft ...");
    var live = new SyncProgress<double>(mbit => OverwriteLine($"{label}: {mbit,6:0.0} Mbit/s"));
    var result = await measure(live, CancellationToken.None);
    OverwriteLine($"{label}: {result.Mbps,6:0.0} Mbit/s");
    Console.WriteLine();
    return result.Mbps;
}

// Überschreibt die aktuelle Konsolenzeile; die feste Breite löscht Reste längerer Ausgaben.
static void OverwriteLine(string text) => Console.Write($"\r{text,-40}");

// Meldet direkt im Thread des Reporters. Progress<T> würde die Callbacks auf den ThreadPool
// posten; ein verspätet ausgeführter Live-Wert könnte dann die finale Zeile überschreiben.
internal sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
