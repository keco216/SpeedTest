using System.Diagnostics;

namespace SpeedTest.Core;

/// <summary>
/// Gemeinsame Ablaufsteuerung für Durchsatzmessungen (Download und Upload):
/// feste Gesamtdauer mit Warm-up-Phase, threadsicherer Byte-Zähler, parallele
/// Transfer-Schleifen und ein Reporter-Loop, der die Live-Geschwindigkeit als
/// gleitenden Durchschnitt über <see cref="SmoothingWindow"/> meldet.
/// </summary>
internal sealed class ThroughputSession
{
    /// <summary>Breite des gleitenden Fensters, über das die Live-Geschwindigkeit
    /// gemittelt wird; das Endergebnis bleibt davon unberührt.</summary>
    private static readonly TimeSpan SmoothingWindow = TimeSpan.FromSeconds(1);

    private readonly TimeSpan _duration;
    private readonly TimeSpan _warmup;
    private readonly TimeSpan _reportInterval;

    private long _bytes;
    private int _failedTransfers;

    public ThroughputSession(TimeSpan? duration = null, TimeSpan? warmup = null, TimeSpan? reportInterval = null)
    {
        _duration = duration ?? TimeSpan.FromSeconds(10);
        _warmup = warmup ?? TimeSpan.FromSeconds(2);
        _reportInterval = reportInterval ?? TimeSpan.FromMilliseconds(200);
    }

    /// <summary>Zählt übertragene Bytes; aus beliebigen Threads aufrufbar.</summary>
    public void AddBytes(long count) => Interlocked.Add(ref _bytes, count);

    /// <summary>
    /// Führt <paramref name="streamCount"/> parallele Transfer-Schleifen aus, bis die
    /// Gesamtdauer abgelaufen ist. <paramref name="transferOnce"/> überträgt jeweils einen
    /// Request und meldet die Bytes über <see cref="AddBytes"/>; endet er vorzeitig, wird er
    /// erneut gestartet. Fehler einzelner Transfers beenden die Messung nicht.
    /// </summary>
    /// <returns>Durchsatz ab Warm-up-Ende und Anzahl fehlgeschlagener Transfers.</returns>
    public async Task<ThroughputResult> RunAsync(
        int streamCount,
        Func<CancellationToken, Task> transferOnce,
        IProgress<double>? liveSpeed,
        CancellationToken ct)
    {
        _bytes = 0;
        _failedTransfers = 0;

        using var timeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeCts.CancelAfter(_duration);
        var token = timeCts.Token;

        var clock = Stopwatch.StartNew();
        long warmupBytes = 0;
        var warmupElapsed = TimeSpan.Zero;
        var warmupDone = false;

        var tasks = new List<Task>(streamCount + 2);
        for (var i = 0; i < streamCount; i++)
            tasks.Add(TransferLoopAsync(transferOnce, token));
        tasks.Add(CaptureWarmupSnapshotAsync());
        tasks.Add(ReportLoopAsync());

        await Task.WhenAll(tasks);
        ct.ThrowIfCancellationRequested();

        var measuredBytes = Interlocked.Read(ref _bytes);
        var measuredTime = clock.Elapsed;
        if (warmupDone)
        {
            measuredBytes -= warmupBytes;
            measuredTime -= warmupElapsed;
        }

        var mbps = measuredTime > TimeSpan.Zero ? ToMbitPerSecond(measuredBytes, measuredTime.TotalSeconds) : 0;
        return new ThroughputResult(mbps, _failedTransfers);

        async Task CaptureWarmupSnapshotAsync()
        {
            if (!await TryDelayAsync(_warmup, token))
                return;

            warmupElapsed = clock.Elapsed;
            warmupBytes = Interlocked.Read(ref _bytes);
            warmupDone = true;
        }

        async Task ReportLoopAsync()
        {
            // Gemeldet wird der Durchschnitt über ein gleitendes Fenster statt des rohen
            // Momentanwerts des letzten Ticks: Einzel-Ticks schwanken durch TCP-Bursts
            // und Request-Neustarts zweistellig, das Fenstermittel hält die Anzeige ruhig.
            // Am Anfang wächst das Fenster erst an — die Rampe bleibt schnell sichtbar.
            var windowTicks = Math.Max(1, (int)Math.Round(SmoothingWindow / _reportInterval));
            var snapshots = new Queue<(TimeSpan Elapsed, long Bytes)>();
            snapshots.Enqueue((TimeSpan.Zero, 0));

            while (await TryDelayAsync(_reportInterval, token))
            {
                var elapsed = clock.Elapsed;
                var bytes = Interlocked.Read(ref _bytes);

                snapshots.Enqueue((elapsed, bytes));
                if (snapshots.Count > windowTicks + 1)
                    snapshots.Dequeue();

                var (startElapsed, startBytes) = snapshots.Peek();
                var window = (elapsed - startElapsed).TotalSeconds;
                if (window > 0)
                    liveSpeed?.Report(ToMbitPerSecond(bytes - startBytes, window));
            }
        }
    }

    private async Task TransferLoopAsync(Func<CancellationToken, Task> transferOnce, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await transferOnce(token);
            }
            catch (OperationCanceledException)
            {
                // Zeit abgelaufen oder Abbruch von außen; die Schleifenbedingung beendet den Loop.
            }
            catch (Exception)
            {
                // Ein einzelner fehlgeschlagener Transfer darf die Messung nicht abbrechen,
                // wird aber gezählt, damit Aufrufer Serverfehler von echten 0-Werten
                // unterscheiden können. Die kurze Pause verhindert eine heiße Fehlerschleife.
                Interlocked.Increment(ref _failedTransfers);
                await TryDelayAsync(TimeSpan.FromMilliseconds(250), token);
            }
        }
    }

    private static async Task<bool> TryDelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static double ToMbitPerSecond(long bytes, double seconds) => bytes * 8 / seconds / 1_000_000;
}
