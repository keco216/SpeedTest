namespace SpeedTest.Core;

/// <summary>Ergebnis einer Ping-Messung.</summary>
/// <param name="AverageMs">Mittlere Round-Trip-Zeit der erfolgreichen Pings in Millisekunden.</param>
/// <param name="JitterMs">Mittlere Differenz aufeinanderfolgender Ping-Zeiten in Millisekunden.</param>
/// <param name="PacketLossPercent">Anteil verlorener Pakete in Prozent (0–100).</param>
public record PingResult(double AverageMs, double JitterMs, double PacketLossPercent);
