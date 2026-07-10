using System.Net;
using System.Net.Http.Headers;

namespace SpeedTest.Core;

/// <summary>
/// <see cref="HttpContent"/>, der Zufallsdaten blockweise direkt in den Request-Stream
/// schreibt und jeden geschriebenen Block sofort meldet — der Gesamtinhalt wird nie
/// am Stück allokiert.
/// </summary>
internal sealed class RandomDataContent : HttpContent
{
    private const int BlockSize = 80 * 1024;

    // Ein einmal gefüllter Block für alle Requests und Streams; er wird nur gelesen,
    // der konkrete Inhalt ist für die Messung egal (Zufall verhindert Kompression).
    private static readonly byte[] Payload = CreatePayload();

    private readonly long _length;
    private readonly Action<long> _onBlockWritten;

    public RandomDataContent(long length, Action<long> onBlockWritten)
    {
        _length = length;
        _onBlockWritten = onBlockWritten;
        Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    }

    private static byte[] CreatePayload()
    {
        var payload = new byte[BlockSize];
        Random.Shared.NextBytes(payload);
        return payload;
    }

    // Die tokenlose Pflicht-Überladung delegiert an die abbrechbare Variante;
    // HttpClient ruft beim Senden die Überladung mit CancellationToken auf.
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(
        Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var remaining = _length;
        while (remaining > 0)
        {
            var count = (int)Math.Min(remaining, Payload.Length);
            await stream.WriteAsync(Payload.AsMemory(0, count), cancellationToken);
            _onBlockWritten(count);
            remaining -= count;
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _length;
        return true;
    }
}
