namespace SpeedTest.Gui.Services;

/// <summary>Ein vollständig durchgelaufener Messlauf für die Historie.</summary>
public record TestResultRecord(
    DateTimeOffset Timestamp,
    double PingMs,
    double JitterMs,
    double PacketLossPercent,
    double DownloadMbps,
    double UploadMbps);
