namespace SpeedTest.Core;

/// <summary>Gemeinsamer <see cref="HttpClient"/> für alle Messungen.</summary>
internal static class SpeedTestHttp
{
    /// <summary>
    /// Ein Client für alle Requests, damit Verbindungen wiederverwendet werden.
    /// Kein Client-Timeout: Die Laufzeit steuert die jeweilige Messung über CancellationTokens.
    /// </summary>
    public static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
}
