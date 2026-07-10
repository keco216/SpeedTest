using System.Diagnostics;

namespace SpeedTest.Core;

/// <summary>
/// Gemeinsame Ablaufsteuerung für Durchsatzmessungen (Download und Upload):
/// feste Gesamtdauer mit Warm-up-Phase, threadsicherer Byte-Zähler, parallele
/// Transfer-Schleifen und ein Reporter-Loop für die Live-Geschwindigkeit.
/// </summary>
internal sealed class ThroughputSession
{
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
            long lastBytes = 0;
            var lastElapsed = TimeSpan.Zero;

            while (await TryDelayAsync(_reportInterval, token))
            {
                var elapsed = clock.Elapsed;
                var bytes = Interlocked.Read(ref _bytes);

                var interval = (elapsed - lastElapsed).TotalSeconds;
                if (interval > 0)
                    liveSpeed?.Report(ToMbitPerSecond(bytes - lastBytes, interval));

                lastBytes = bytes;
                lastElapsed = elapsed;
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
