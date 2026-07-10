using System.Net.NetworkInformation;

namespace SpeedTest.Core;

/// <summary>Misst Latenz, Jitter und Paketverlust per ICMP-Ping.</summary>
public class PingMeter
{
    private const int TimeoutMs = 2000;

    /// <summary>
    /// Sendet <paramref name="samples"/> Pings nacheinander an <paramref name="host"/>.
    /// Timeouts und Netzwerkfehler zählen als Paketverlust und werfen keine Exception;
    /// ein Abbruch über <paramref name="ct"/> wirft eine OperationCanceledException.
    /// </summary>
    public async Task<PingResult> MeasureAsync(string host = "1.1.1.1", int samples = 5, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples);

        var roundtrips = new List<long>(samples);
        var lost = 0;

        using var ping = new Ping();

        for (var i = 0; i < samples; i++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(TimeoutMs), cancellationToken: ct);
                if (reply.Status == IPStatus.Success)
                    roundtrips.Add(reply.RoundtripTime);
                else
                    lost++;
            }
            catch (PingException)
            {
                // DNS-/Netzwerkfehler werden wie ein Timeout als Verlust gewertet.
                lost++;
            }
        }

        var averageMs = roundtrips.Count > 0 ? roundtrips.Average() : 0d;

        var jitterMs = 0d;
        if (roundtrips.Count > 1)
        {
            var diffSum = 0d;
            for (var i = 1; i < roundtrips.Count; i++)
                diffSum += Math.Abs(roundtrips[i] - roundtrips[i - 1]);
            jitterMs = diffSum / (roundtrips.Count - 1);
        }

        var packetLossPercent = 100d * lost / samples;

        return new PingResult(averageMs, jitterMs, packetLossPercent);
    }
}
