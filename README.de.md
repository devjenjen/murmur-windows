# Murmur

[English](README.md) | [Deutsch](README.de.md) | [Changelog](CHANGELOG.md)

Murmur ist ein Push-to-Talk-Diktierprogramm für Windows. Gewählte Taste gedrückt halten, sprechen, loslassen: Der bereinigte Text wird in die Anwendung mit Fokus eingefügt.

Dies ist eine KI-unterstützte Windows-Anpassung, inspiriert von [per-simmons/murmur-youtube](https://github.com/per-simmons/murmur-youtube). Sie beansprucht nicht, die Upstream-Implementierung zu sein.

## Download und erster Start

Auf der GitHub-Releases-Seite gibt es zwei x64-Dateien:

- `Murmur-Setup-vX.Y.Z.exe` installiert Murmur für das aktuelle Benutzerkonto.
- `Murmur-<version>-win-x64.zip` ist portabel. Archiv entpacken und `Murmur.App.exe` starten; es wird nichts installiert.

Der Installer ist nicht signiert. Windows SmartScreen kann deshalb eine Warnung anzeigen. Beim ersten Start öffnet Murmur die Einrichtung. Mikrofon, Taste und Sprachmodell wählen, dann das gewählte Modell herunterladen. Erst danach ist Diktieren möglich.

Murmur prüft beim Start auf eine neuere stabile Release. Bei einem Treffer öffnet ein Eintrag im Tray-Menü die zugehörige Release-Seite. Updates werden nicht selbst installiert.

## Lokale Daten

| Pfad | Inhalt |
|---|---|
| `%LOCALAPPDATA%\Murmur\settings.json` | Einstellungen |
| `%LOCALAPPDATA%\Murmur\history.jsonl` | Diktatverlauf |
| `%LOCALAPPDATA%\Murmur\murmur.log` | Anwendungsprotokoll |
| `%LOCALAPPDATA%\Murmur\models\` | heruntergeladene Sprachmodelle |
| `%LOCALAPPDATA%\Programs\Murmur` | Installation durch die Setup EXE |

## Funktion

- Nimmt Mikrofon-Audio auf, solange die Push-to-Talk-Taste gedrückt ist.
- Nutzt Parakeet v3 für fertige Transkripte oder Nemotron 3.5 für Live-Vorschauen.
- Formatiert Text nach festen Regeln und wendet persönliche Wörterbuchkorrekturen an.
- Zeigt ein Aufnahmefenster, ohne dem Zielprogramm den Fokus zu nehmen.

Die Sprachmodelle laufen über sherpa-onnx. Immer nur ein Modell ist gleichzeitig im Speicher. Murmur verwendet .NET 10 und Avalonia, die Audioaufnahme läuft über WASAPI mit NAudio.

Die Einstellungen verwenden das Lens-Redesign für Einrichtung, Verlauf und Wörterbuch.

### Optionales Audiofeedback

Eine ElevenLabs-Feedback-WAV-Datei ist weder im Repository noch in der Setup-
EXE oder im Portable-ZIP enthalten. Für Audiofeedback kann eine eigene,
lizenzierte `Sounds\feedback.wav` neben `Murmur.App.exe` abgelegt werden. Fehlt
die Datei, bleibt das Feedback stumm. Für die Lizenz des Tons sind die Nutzer
selbst verantwortlich; die MIT-Lizenz von Murmur gilt nur für den Murmur-
Quellcode, nicht für nutzereigene Töne. Relevante ElevenLabs-Links stehen in
[NOTICE.md](NOTICE.md).

## Grenzen

Die Transkriptionsqualität ist nicht gemessen. Murmur macht keine Genauigkeitsangabe und stützt die Standardauswahl auf keinen Benchmark.

CI kann nicht beweisen, dass Texte in eine echte Anwendung im Vordergrund eingefügt werden. Das muss auf einem Windows-Rechner von einer Person geprüft werden.

## Lizenz

Der Murmur-Quellcode steht unter der MIT-Lizenz. Hinweise zu Modellen und gebündelten Drittanbieterkomponenten stehen in [NOTICE.md](NOTICE.md).

## Build

```powershell
cd windows
dotnet restore Murmur.sln
dotnet build Murmur.sln --configuration Release --no-incremental -warnaserror
dotnet test Murmur.sln --configuration Release --no-build
```

Das blockierende Release-Gate nimmt Tests mit der Kategorie `RealModel` aus.
Der separate Workflow `.github/workflows/real-model.yml` führt diese Modell-
Download- und Inferenztests manuell oder nächtlich aus.

Weitere Details: [Windows-Entwicklungsnotizen](windows/README.md) und [Hinweise zu Sprachmodellen](docs/PARAKEET-WINDOWS.md).
