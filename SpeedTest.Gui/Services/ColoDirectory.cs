using System.Diagnostics.CodeAnalysis;

namespace SpeedTest.Gui.Services;

/// <summary>Standortdaten eines Cloudflare-Rechenzentrums.</summary>
public record ColoInfo(string City, string Country, double Latitude, double Longitude);

/// <summary>Bekannte Cloudflare-Standorte (IATA-Codes) mit Stadt, Land und Koordinaten.</summary>
public static class ColoDirectory
{
    private static readonly Dictionary<string, ColoInfo> Colos = new()
    {
        ["FRA"] = new("Frankfurt", "Deutschland", 50.11, 8.68),
        ["MUC"] = new("München", "Deutschland", 48.14, 11.58),
        ["AMS"] = new("Amsterdam", "Niederlande", 52.37, 4.90),
        ["CDG"] = new("Paris", "Frankreich", 48.86, 2.35),
        ["LHR"] = new("London", "Vereinigtes Königreich", 51.51, -0.13),
        ["VIE"] = new("Wien", "Österreich", 48.21, 16.37),
        ["ZRH"] = new("Zürich", "Schweiz", 47.37, 8.54),
        ["DUS"] = new("Düsseldorf", "Deutschland", 51.23, 6.78),
        ["HAM"] = new("Hamburg", "Deutschland", 53.55, 9.99),
    };

    public static bool TryGet(string colo, [NotNullWhen(true)] out ColoInfo? info)
        => Colos.TryGetValue(colo, out info);
}
