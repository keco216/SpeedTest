namespace SpeedTest.Core;

/// <summary>Ermittelt Standort des Messservers und Client-IP über Cloudflares Trace-Endpoint.</summary>
public class TraceClient
{
    private const string TraceUrl = "https://speed.cloudflare.com/cdn-cgi/trace";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Ruft die Trace-Info ab (eigenes 3-s-Timeout). Die Info ist rein informativ:
    /// Bei jedem Fehler — Timeout, Netzfehler, unerwartetes Format, Abbruch — kommt
    /// <c>null</c> zurück, es wird nie geworfen.
    /// </summary>
    public async Task<TraceInfo?> GetAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Timeout);

            var text = await SpeedTestHttp.Client.GetStringAsync(TraceUrl, timeoutCts.Token)
                .ConfigureAwait(false);

            string? colo = null, ip = null;
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                var key = line[..separator];
                var value = line[(separator + 1)..];
                if (key == "colo")
                    colo = value;
                else if (key == "ip")
                    ip = value;
            }

            return colo is { Length: > 0 } && ip is { Length: > 0 }
                ? new TraceInfo(colo, ip)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
