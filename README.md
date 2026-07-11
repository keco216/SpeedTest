# SpeedTest

Ein Internet-Speedtest für Windows: misst Ping, Jitter, Paketverlust sowie Download- und
Upload-Geschwindigkeit — als moderne WPF-App im Windows-11-Look (Dark/Light) und als
Konsolenversion. Die Messlogik steckt in einer wiederverwendbaren .NET-Bibliothek.

![Screenshot](docs/screenshot.png)

## Features

- **Ping-Messung** per ICMP: Latenz, Jitter (mittlere Differenz aufeinanderfolgender
  Pings) und Paketverlust
- **Download & Upload** über parallele HTTP-Streams mit Warm-up-Phase und Live-Werten
- **Animierter Tacho** mit nichtlinearer Skala (0–1000 Mbit/s), weich zählender Zahl
  und Fortschrittsbogen
- **Abbrechen** jederzeit möglich; fertige Teilergebnisse bleiben stehen
- **Ergebnis-Historie** (letzte 50 Läufe, `%AppData%\SpeedTest\history.json`) mit
  Undo beim Löschen statt Bestätigungsdialog
- **Messserver-Info** mit Detail-Popup: Standort, eigene IP (kopierbar), Mini-Karte
  (OpenStreetMap) und Google-Maps-Link
- **Ergebnis-Export** in die Zwischenablage
- **Dark-/Light-Mode** zur Laufzeit umschaltbar, inklusive Titelleiste
- **Auto-Update**: prüft beim Start auf ein neueres GitHub-Release und installiert
  es auf Klick (MSI mit einmaliger UAC-Bestätigung, danach startet die App neu)
- **Ehrliche Fehlanzeige**: Lehnt der Testserver Anfragen ab (Rate-Limit), zeigt die
  App „fehlgeschlagen" statt irreführender 0,0-Werte

## Projekte

| Projekt | Beschreibung |
|---|---|
| `SpeedTest.Core` | Messlogik (Klassenbibliothek, ohne UI-Abhängigkeiten) |
| `SpeedTest.Cli` | Konsolenversion mit Live-Anzeige |
| `SpeedTest.Gui` | WPF-App (net10.0-windows) |

## Bauen & Starten

Voraussetzungen: Windows, [.NET-10-SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

```powershell
# GUI starten
dotnet run --project SpeedTest.Gui

# Konsolenversion starten
dotnet run --project SpeedTest.Cli
```

## Veröffentlichen (Publish & Installer)

`publish.cmd` baut die eigenständige Single-File-EXE (bringt die .NET-Runtime mit)
und daraus den MSI-Installer (WiX Toolset v7):

```powershell
.\publish.cmd
```

Ergebnis: `installer\bin\Release\SpeedTest-<Version>-x64.msi` — installiert nach
`C:\Program Files\Speedtest` samt Startmenü-Eintrag. Fertige Installer gibt es
unter [Releases](https://github.com/keco216/SpeedTest/releases).

Für ein neues Release: Version in `SpeedTest.Gui\SpeedTest.Gui.csproj` **und**
`installer\SpeedTest.Installer.wixproj` erhöhen, `publish.cmd` ausführen und das MSI
als Asset eines GitHub-Releases mit Tag `vX.Y.Z` veröffentlichen — installierte Apps
bieten es beim nächsten Start automatisch als Update an.

## Hinweis zur Messung

Gemessen wird gegen die öffentlichen Speedtest-Endpunkte von **Cloudflare**
(`speed.cloudflare.com`). Die Ergebnisse hängen damit auch von der Anbindung an das
nächstgelegene Cloudflare-Rechenzentrum ab (die App zeigt den Standort an). Bei sehr
vielen Messungen in kurzer Zeit kann Cloudflare Anfragen vorübergehend ablehnen
(Rate-Limit) — die App zeigt die betroffene Phase dann als fehlgeschlagen an; nach
einigen Minuten funktioniert die Messung wieder.

## Lizenz

[MIT](LICENSE)
