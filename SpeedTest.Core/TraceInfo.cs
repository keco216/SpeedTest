namespace SpeedTest.Core;

/// <summary>Verbindungsinfo des Testservers aus cdn-cgi/trace.</summary>
/// <param name="Colo">IATA-Code des Cloudflare-Rechenzentrums (z. B. "FRA").</param>
/// <param name="ClientIp">Öffentliche IP-Adresse des Clients.</param>
public record TraceInfo(string Colo, string ClientIp);
