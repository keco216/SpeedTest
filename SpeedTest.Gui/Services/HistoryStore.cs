using System.IO;
using System.Text.Json;

namespace SpeedTest.Gui.Services;

/// <summary>
/// Persistiert die letzten Messläufe als von Hand lesbares JSON unter
/// %AppData%\SpeedTest\history.json. Die Historie ist Komfort: Lesefehler führen
/// nie zu einer Exception beim Aufrufer, sondern zu einer leeren Liste.
/// </summary>
public sealed class HistoryStore
{
    private const int MaxEntries = 50;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public HistoryStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SpeedTest", "history.json"))
    {
    }

    /// <summary>Eigener Speicherort, z. B. für Tests.</summary>
    public HistoryStore(string filePath) => _filePath = filePath;

    public async Task<List<TestResultRecord>> LoadAsync()
    {
        try
        {
            if (!File.Exists(_filePath))
                return [];

            using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<TestResultRecord>>(stream, JsonOptions)
                .ConfigureAwait(false) ?? [];
        }
        catch (Exception)
        {
            // Fehlende, gesperrte oder defekte Datei: leere Historie statt Fehler.
            return [];
        }
    }

    /// <summary>Hängt einen Lauf an und kürzt auf die neuesten <see cref="MaxEntries"/>.</summary>
    public async Task AddAsync(TestResultRecord record)
    {
        var entries = await LoadAsync().ConfigureAwait(false);
        entries.Add(record);
        if (entries.Count > MaxEntries)
            entries.RemoveRange(0, entries.Count - MaxEntries);

        await SaveAsync(entries).ConfigureAwait(false);
    }

    /// <summary>Leert die Historie (schreibt atomar eine leere Liste).</summary>
    public Task ClearAsync() => SaveAsync([]);

    private async Task SaveAsync(List<TestResultRecord> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        // Atomar: erst vollständig in eine temporäre Datei schreiben, dann ersetzen —
        // ein Absturz mitten im Schreiben zerstört so keine bestehende Historie.
        var tmpPath = _filePath + ".tmp";
        using (var stream = File.Create(tmpPath))
        {
            await JsonSerializer.SerializeAsync(stream, entries, JsonOptions).ConfigureAwait(false);
        }

        // Virenscanner und Indexer halten frisch geschriebene Dateien kurz offen,
        // dann schlägt das Ersetzen transient fehl — kurz verzögert wiederholen.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tmpPath, _filePath, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 4)
            {
                await Task.Delay(100 * attempt).ConfigureAwait(false);
            }
        }
    }
}
