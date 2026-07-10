namespace SpeedTest.Core;

/// <summary>Ergebnis einer Durchsatzmessung.</summary>
/// <param name="Mbps">Durchsatz in Mbit/s, gerechnet ab dem Ende der Warm-up-Phase.</param>
/// <param name="FailedTransfers">Anzahl fehlgeschlagener Einzeltransfers (z. B. vom
/// Server abgelehnte Requests); erlaubt Aufrufern, ein 0-Ergebnis wegen Serverfehlern
/// von einer echten Messung zu unterscheiden.</param>
public record ThroughputResult(double Mbps, int FailedTransfers);
