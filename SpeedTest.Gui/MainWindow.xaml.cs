using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SpeedTest.Core;
using SpeedTest.Gui.Controls;
using SpeedTest.Gui.Interop;
using SpeedTest.Gui.Services;

namespace SpeedTest.Gui;

public partial class MainWindow : Window
{
    private const string FailedTransferTooltip =
        "Messung fehlgeschlagen – der Server hat Anfragen abgelehnt (mögliches Rate-Limit). " +
        "Später erneut versuchen.";

    // Segoe-Fluent-Glyphen der Kopieren-Buttons (Copy bzw. CheckMark)
    private const string CopyGlyph = "\uE8C8";
    private const string CheckGlyph = "\uE73E";

    private readonly HistoryStore _historyStore = new();
    private readonly MiniMapService _miniMap = new();
    private readonly CopyButtonFeedback _resultCopyFeedback;
    private readonly CopyButtonFeedback _ipCopyFeedback;

    private bool _isRunning;
    private CancellationTokenSource? _cts;
    private TraceInfo? _traceInfo;
    private string? _serverDisplay;
    private CompletedRun? _lastRun;
    private DateTime _serverPopupClosedAt;

    // Undo-Frist nach dem Löschen-Klick: Die Datei wird erst angefasst, wenn der
    // Timer abläuft (oder ein neuer Lauf/das Schließen den Clear bestätigt).
    private bool _isClearPending;
    private DispatcherTimer? _pendingClearTimer;

    public MainWindow()
    {
        InitializeComponent();
        _resultCopyFeedback = new CopyButtonFeedback(CopyResultButton);
        _ipCopyFeedback = new CopyButtonFeedback(CopyIpButton);

        SourceInitialized += (_, _) => DarkTitleBar.Set(this, ThemeManager.IsDark);
        Loaded += async (_, _) => await RefreshHistoryAsync();
        Closing += OnClosing;

        // Flyout rechtsbündig unter der Serverzeile ausrichten
        ServerPopup.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
            [new CustomPopupPlacement(
                new Point(targetSize.Width - popupSize.Width, targetSize.Height + 6),
                PopupPrimaryAxis.Horizontal)];
        ServerPopup.Closed += (_, _) => _serverPopupClosedAt = DateTime.UtcNow;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && ServerPopup.IsOpen)
            {
                ServerPopup.IsOpen = false;
                e.Handled = true;
            }
        };
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        // Tauscht das Farb-Dictionary; alle DynamicResource-Verweise färben live um.
        ThemeManager.Apply(!ThemeManager.IsDark);
        DarkTitleBar.Set(this, ThemeManager.IsDark);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_isClearPending)
            return;

        // Wer löscht und sofort schließt, erwartet beim nächsten Start eine leere
        // Historie. Synchron warten, weil der Prozess gleich endet und ein
        // Fire-and-forget-Task abgewürgt würde; deadlock-frei, da der Store
        // durchgehend ConfigureAwait(false) verwendet.
        _isClearPending = false;
        _pendingClearTimer?.Stop();
        _historyStore.ClearAsync().GetAwaiter().GetResult();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            // Abbrechen-Klick: Button sperren, bis der Ablauf wirklich beendet ist —
            // das finally unten stellt ihn wieder her. Kein Doppel-Cancel möglich,
            // weil der gesperrte Button keine weiteren Klicks liefert.
            StartButton.IsEnabled = false;
            StartButton.Content = "Wird abgebrochen …";
            _cts?.Cancel();
            return;
        }

        _isRunning = true;
        var cts = new CancellationTokenSource();
        _cts = cts;

        // Ein neuer Lauf während der Undo-Frist bestätigt den ausstehenden Clear
        // sofort — die Historie enthält danach nur die neuen Läufe.
        if (_isClearPending)
            await CommitPendingClearAsync();

        StartButton.Content = "Abbrechen";
        StartButton.Style = (Style)FindResource("CancelButtonStyle");
        PingValueText.Text = JitterValueText.Text = DownloadValueText.Text = UploadValueText.Text = "–";
        DownloadValueText.ToolTip = UploadValueText.ToolTip = null;
        CopyResultButton.Visibility = Visibility.Collapsed;
        Gauge.Value = 0;
        Gauge.IsActive = true;

        // Serverinfo parallel zur Ping-Phase abrufen; einmal ermittelt, bleibt sie
        // für die Sitzung stehen. GetAsync wirft nie, der Task braucht kein Fangnetz.
        var traceTask = ServerInfoText.Visibility == Visibility.Visible
            ? null
            : new TraceClient().GetAsync(cts.Token);

        // Bewusst Progress<T>: Im UI-Thread erzeugt, stellt es die Callbacks über den
        // erfassten SynchronizationContext zu — die Animationen starten damit im UI-Thread.
        var liveSpeed = new Progress<double>(mbit => Gauge.Value = mbit);

        try
        {
            SetStatus("●", "Ping wird gemessen …");
            var ping = await new PingMeter().MeasureAsync(ct: cts.Token);
            ShowResult(PingColumn, PingValueText, SpeedGauge.FormatValue(ping.AverageMs));
            ShowResult(JitterColumn, JitterValueText, SpeedGauge.FormatValue(ping.JitterMs));

            if (traceTask is not null)
                ShowServerInfo(await traceTask);

            SetStatus("↓", "Download läuft …");
            var download = await new DownloadMeter().MeasureAsync(liveSpeed, cts.Token);
            var downloadFailed = IsThroughputFailed(download);
            if (downloadFailed)
                ShowFailedResult(DownloadColumn, DownloadValueText);
            else
                ShowResult(DownloadColumn, DownloadValueText, SpeedGauge.FormatValue(download.Mbps));
            Gauge.Value = 0;

            SetStatus("↑", "Upload läuft …");
            var upload = await new UploadMeter().MeasureAsync(liveSpeed, cts.Token);
            var uploadFailed = IsThroughputFailed(upload);
            if (uploadFailed)
                ShowFailedResult(UploadColumn, UploadValueText);
            else
                ShowResult(UploadColumn, UploadValueText, SpeedGauge.FormatValue(upload.Mbps));

            // Der Lauf ist abgeschlossen (auch mit fehlgeschlagener Phase): Zeitstempel
            // einmal festhalten — Export und Historie verwenden denselben — und den
            // Kopieren-Button freischalten.
            var completedAt = DateTimeOffset.Now;
            _lastRun = new CompletedRun(completedAt, ping, download, upload, downloadFailed, uploadFailed, _serverDisplay);
            CopyResultButton.Visibility = Visibility.Visible;

            if (downloadFailed || uploadFailed)
            {
                var failedPhases = (downloadFailed, uploadFailed) switch
                {
                    (true, true) => "Download und Upload",
                    (true, false) => "Download",
                    _ => "Upload",
                };
                SetStatus("!", $"Fertig – {failedPhases} fehlgeschlagen");
            }
            else
            {
                SetStatus("✓", "Fertig");

                // Nur Läufe mit zwei gültigen Durchsatz-Phasen landen in der Historie —
                // nie wieder ein 0,0-Eintrag. Ein Speicherfehler darf den erfolgreichen
                // Lauf nicht als Fehler anzeigen.
                try
                {
                    await _historyStore.AddAsync(new TestResultRecord(
                        completedAt,
                        ping.AverageMs, ping.JitterMs, ping.PacketLossPercent,
                        download.Mbps, upload.Mbps));
                    await RefreshHistoryAsync();
                }
                catch (Exception)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Gewollter Abbruch, kein Fehlerzustand; fertige Teilergebnisse bleiben stehen.
            SetStatus("✕", "Abgebrochen");
        }
        catch (Exception ex)
        {
            SetStatus("!", $"Fehler: {ex.Message}");
        }
        finally
        {
            cts.Dispose();
            _cts = null;
            Gauge.Value = 0;
            Gauge.IsActive = false;
            _isRunning = false;
            StartButton.Content = "Start";
            StartButton.Style = (Style)FindResource("StartButtonStyle");
            StartButton.IsEnabled = true;
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var count = HistoryList.Items.Count;
        if (_isClearPending || count == 0)
            return;

        _isClearPending = true;
        ClearHistoryButton.Visibility = Visibility.Collapsed;

        // Zeilen gestaffelt ausblenden; nach der letzten erscheint die Undo-Zeile.
        for (var i = 0; i < count; i++)
        {
            if (HistoryList.ItemContainerGenerator.ContainerFromIndex(i) is not UIElement row)
                continue;

            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150))
            {
                BeginTime = TimeSpan.FromMilliseconds(40 * i),
            };
            if (i == count - 1)
            {
                fadeOut.Completed += (_, _) =>
                {
                    // Kann nach einem zwischenzeitlichen Commit (neuer Messlauf) feuern —
                    // dann gibt es keinen Undo-Zustand mehr anzuzeigen.
                    if (!_isClearPending)
                        return;

                    HistoryList.Visibility = Visibility.Collapsed;
                    UndoPanel.Visibility = Visibility.Visible;
                    FadeSlideIn(UndoPanel);
                };
            }
            row.BeginAnimation(OpacityProperty, fadeOut);
        }

        _pendingClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pendingClearTimer.Tick += async (_, _) => await CommitPendingClearAsync();
        _pendingClearTimer.Start();
    }

    private void UndoClear_Click(object sender, RoutedEventArgs e)
    {
        if (!_isClearPending)
            return;

        _isClearPending = false;
        _pendingClearTimer?.Stop();
        _pendingClearTimer = null;

        // Nichts wurde gelöscht — die Datei blieb unangetastet, die Zeilen kommen
        // mit der vorhandenen Einblende-Animation zurück.
        UndoPanel.Visibility = Visibility.Collapsed;
        HistoryList.Visibility = Visibility.Visible;
        ClearHistoryButton.Visibility = Visibility.Visible;

        for (var i = 0; i < HistoryList.Items.Count; i++)
        {
            if (HistoryList.ItemContainerGenerator.ContainerFromIndex(i) is UIElement row)
                FadeSlideIn(row);
        }
    }

    /// <summary>Führt den ausstehenden Clear aus und setzt die Karte zurück.</summary>
    private async Task CommitPendingClearAsync()
    {
        if (!_isClearPending)
            return;

        _isClearPending = false;
        _pendingClearTimer?.Stop();
        _pendingClearTimer = null;

        await _historyStore.ClearAsync();

        UndoPanel.Visibility = Visibility.Collapsed;
        HistoryList.ItemsSource = null;
        HistoryList.Visibility = Visibility.Visible;
        ClearHistoryButton.Visibility = Visibility.Visible;
        HistoryCard.Visibility = Visibility.Collapsed;
    }

    private async Task RefreshHistoryAsync()
        => ShowHistory(await _historyStore.LoadAsync());

    private void ShowHistory(List<TestResultRecord> entries)
    {
        var rows = entries
            .OrderByDescending(r => r.Timestamp)
            .Take(5)
            .Select(r => new HistoryRow(
                r.Timestamp.ToString("dd.MM. HH:mm"),
                $"↓ {SpeedGauge.FormatValue(r.DownloadMbps)}",
                $"↑ {SpeedGauge.FormatValue(r.UploadMbps)}",
                $"{SpeedGauge.FormatValue(r.PingMs)} ms"))
            .ToList();

        HistoryList.ItemsSource = rows;
        HistoryCard.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Anzeigezeile der Historie; das DataTemplate bindet an die Properties.</summary>
    private sealed record HistoryRow(string Time, string Download, string Upload, string Ping);

    /// <summary>Mbps ≈ 0 bei abgelehnten Requests: kein Messwert, sondern ein Fehlschlag.</summary>
    private static bool IsThroughputFailed(ThroughputResult result)
        => result.Mbps < 0.1 && result.FailedTransfers > 0;

    /// <summary>Zeigt den Standort des Messservers in der Kopfzeile an.</summary>
    private void ShowServerInfo(TraceInfo? trace)
    {
        if (trace is null)
            return;

        _traceInfo = trace;
        _serverDisplay = ColoDirectory.TryGet(trace.Colo, out var colo)
            ? $"{colo.City} ({trace.Colo})"
            : trace.Colo;
        ServerInfoText.Text = $"Server: {_serverDisplay}";
        ServerInfoText.Visibility = Visibility.Visible;
        FadeSlideIn(ServerInfoText);
    }

    private async void ServerInfo_Click(object sender, MouseButtonEventArgs e)
    {
        // Klick auf die Zeile bei offenem Popup: StaysOpen=False hat es beim MouseDown
        // gerade geschlossen — nicht sofort wieder öffnen (Toggle statt Flackern).
        if ((DateTime.UtcNow - _serverPopupClosedAt).TotalMilliseconds < 250)
            return;

        OpenServerPopup();
        await LoadPopupMapAsync();
    }

    /// <summary>Lädt die Mini-Karte nach dem Öffnen nach; das Popup wartet nicht darauf.</summary>
    private async Task LoadPopupMapAsync()
    {
        if (_traceInfo is null || !ColoDirectory.TryGet(_traceInfo.Colo, out var colo))
            return;

        var map = await _miniMap.GetMapAsync(colo.Latitude, colo.Longitude, 240, 120);

        // Beim ersten Öffnen kann das Popup schon wieder zu sein, bis die Tiles da sind;
        // dank Cache erscheint die Karte beim nächsten Öffnen sofort.
        if (map is null || !ServerPopup.IsOpen)
            return;

        PopupMapBrush.ImageSource = map;
        PopupMapPanel.Visibility = Visibility.Visible;
        FadeSlideIn(PopupMapPanel);
    }

    private void OpenServerPopup()
    {
        if (_traceInfo is null)
            return;

        var known = ColoDirectory.TryGet(_traceInfo.Colo, out var colo);
        PopupTitleText.Text = known ? $"{colo!.City}, {colo.Country}" : _traceInfo.Colo;
        PopupColoText.Text = known
            ? $"Cloudflare-Rechenzentrum · {_traceInfo.Colo}"
            : "Cloudflare-Rechenzentrum";
        PopupIpText.Text = $"Deine IP: {_traceInfo.ClientIp}";

        PopupPingText.Visibility = _lastRun is null ? Visibility.Collapsed : Visibility.Visible;
        if (_lastRun is not null)
            PopupPingText.Text = $"Ping zuletzt: {SpeedGauge.FormatValue(_lastRun.Ping.AverageMs)} ms";

        // Karte erscheint erst, wenn LoadPopupMapAsync sie (aus Cache oder Netz) hat
        PopupMapPanel.Visibility = Visibility.Collapsed;

        // Ohne Koordinaten kein Kartenlink (und keine verwaiste Trennlinie)
        var mapVisibility = known ? Visibility.Visible : Visibility.Collapsed;
        PopupSeparator.Visibility = mapVisibility;
        PopupMapLine.Visibility = mapVisibility;

        ServerPopupContent.Opacity = 0;
        ServerPopup.IsOpen = true;
        ServerPopupContent.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }

    private async void CopyIp_Click(object sender, RoutedEventArgs e)
    {
        if (_traceInfo is null)
            return;

        if (await TrySetClipboardTextAsync(_traceInfo.ClientIp))
            _ipCopyFeedback.Show();
        else
            SetStatus("!", "Kopieren fehlgeschlagen – Zwischenablage belegt");
    }

    private void OpenMap_Click(object sender, RoutedEventArgs e) => OpenMapInBrowser();

    private void MapPanel_Click(object sender, MouseButtonEventArgs e) => OpenMapInBrowser();

    private void OpenMapInBrowser()
    {
        if (_traceInfo is null || !ColoDirectory.TryGet(_traceInfo.Colo, out var colo))
            return;

        // Invariant formatieren: deutsches Windows würde "48,14" mit Komma in die URL
        // bauen, und Google Maps versteht das nicht.
        var lat = colo.Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = colo.Longitude.ToString(CultureInfo.InvariantCulture);
        var url = $"https://www.google.com/maps/search/?api=1&query={lat},{lon}";

        try
        {
            // UseShellExecute ist Pflicht: ohne wirft .NET bei URLs eine Win32Exception.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            SetStatus("!", "Browser konnte nicht geöffnet werden");
        }

        ServerPopup.IsOpen = false;
    }

    private async void CopyResult_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRun is null)
            return;

        if (await TrySetClipboardTextAsync(BuildExportText(_lastRun)))
            _resultCopyFeedback.Show();
        else
            SetStatus("!", "Kopieren fehlgeschlagen – Zwischenablage belegt");
    }

    /// <summary>Mehrzeiliger Export; die Werte nutzen dieselbe Formatfunktion wie die Anzeige.</summary>
    private static string BuildExportText(CompletedRun run)
    {
        var header = $"Speedtest {run.Timestamp:dd.MM.yyyy, HH:mm}";
        if (run.Server is not null)
            header += $" – Server: {run.Server}";

        var download = run.DownloadFailed ? "fehlgeschlagen" : $"{SpeedGauge.FormatValue(run.Download.Mbps)} Mbit/s";
        var upload = run.UploadFailed ? "fehlgeschlagen" : $"{SpeedGauge.FormatValue(run.Upload.Mbps)} Mbit/s";

        return string.Join(Environment.NewLine,
            header,
            $"Ping: {SpeedGauge.FormatValue(run.Ping.AverageMs)} ms | " +
            $"Jitter: {SpeedGauge.FormatValue(run.Ping.JitterMs)} ms | " +
            $"Verlust: {SpeedGauge.FormatValue(run.Ping.PacketLossPercent)} %",
            $"Download: {download} | Upload: {upload}");
    }

    /// <summary>
    /// Die Zwischenablage ist ein exklusives Systemobjekt — hält ein anderes Programm sie
    /// gerade offen (Remote-Desktop, Clipboard-Manager), wirft SetText eine
    /// ExternalException. Bis zu 3 Versuche mit 100 ms Abstand, dann false.
    /// </summary>
    private static async Task<bool> TrySetClipboardTextAsync(string text)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException)
            {
                if (attempt < 3)
                    await Task.Delay(100);
            }
        }

        return false;
    }

    /// <summary>
    /// Haken-Feedback eines Kopieren-Buttons: Die Glyphe wechselt 1,5 s auf den Akzent-Haken
    /// und zurück; ein erneuter Klick während der Frist startet die Frist neu.
    /// </summary>
    private sealed class CopyButtonFeedback
    {
        private readonly Button _button;
        private readonly DispatcherTimer _timer;

        public CopyButtonFeedback(Button button)
        {
            _button = button;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                _button.Content = CopyGlyph;
                // Lokalen Wert entfernen, damit Style-Foreground und Hover-Trigger wieder greifen.
                _button.ClearValue(ForegroundProperty);
            };
        }

        public void Show()
        {
            _button.Content = CheckGlyph;
            _button.Foreground = (Brush)_button.FindResource("AccentBrush");
            _timer.Stop();
            _timer.Start();
        }
    }

    /// <summary>Alle Daten eines abgeschlossenen Laufs für den Export.</summary>
    private sealed record CompletedRun(
        DateTimeOffset Timestamp,
        PingResult Ping,
        ThroughputResult Download,
        ThroughputResult Upload,
        bool DownloadFailed,
        bool UploadFailed,
        string? Server);

    /// <summary>Schreibt ein Teilergebnis und blendet seine Spalte eingeschoben ein.</summary>
    private static void ShowResult(UIElement column, TextBlock valueText, string value)
    {
        valueText.Text = value;
        FadeSlideIn(column);
    }

    /// <summary>Markiert eine Durchsatz-Spalte als fehlgeschlagen ("–" mit Erklärung).</summary>
    private static void ShowFailedResult(UIElement column, TextBlock valueText)
    {
        valueText.Text = "–";
        valueText.ToolTip = FailedTransferTooltip;
        FadeSlideIn(column);
    }

    /// <summary>Blendet ein Element ein und schiebt es dabei 8 px nach oben.</summary>
    private static void FadeSlideIn(UIElement element)
    {
        var slide = new TranslateTransform(0, 8);
        element.RenderTransform = slide;

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
    }

    /// <summary>Wechselt die Statuszeile weich: 120 ms aus, Symbol+Text tauschen, 120 ms ein.</summary>
    private void SetStatus(string icon, string message)
    {
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
        fadeOut.Completed += (_, _) =>
        {
            StatusIconRun.Text = icon.Length > 0 ? icon + " " : "";
            StatusMessageRun.Text = message;
            // Die Zeile ist fest einzeilig und schneidet lange Texte ab;
            // der Tooltip zeigt die vollständige Meldung.
            StatusText.ToolTip = message;
            StatusText.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        };
        StatusText.BeginAnimation(OpacityProperty, fadeOut);
    }
}
