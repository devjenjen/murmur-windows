# Murmur

[English](README.md) | [Deutsch](README.de.md) | [Changelog](CHANGELOG.md)

Murmur ist ein Push-to-Talk-Diktierprogramm für Windows. Gewählte Taste gedrückt halten, sprechen, loslassen: Der bereinigte Text wird in die Anwendung mit Fokus eingefügt.

Dies ist eine KI-unterstützte Windows-Anpassung, inspiriert von [per-simmons/murmur-youtube](https://github.com/per-simmons/murmur-youtube). Sie beansprucht nicht, die Upstream-Implementierung zu sein.

## Download und erster Start

Murmur 0.2.0 ist ein Beta-Draft-Kandidat, keine stabile Release. Auf der
[GitHub-Releases-Seite](https://github.com/devjenjen/murmur-windows/releases)
nur ausdrücklich veröffentlichte Assets wählen:

- `Murmur-Setup-vX.Y.Z.exe` installiert Murmur für das aktuelle Benutzerkonto.
- `Murmur-<version>-win-x64.zip` ist portabel. Archiv entpacken und `Murmur.App.exe` starten; es wird nichts installiert.
- `build-info.json` und `SHA256SUMS.txt` beschreiben und prüfen den Kandidaten.

Der Installer ist nicht signiert. Windows SmartScreen kann deshalb eine Warnung anzeigen. Beim ersten Start öffnet Murmur die Einrichtung. Mikrofon, Taste und Sprachmodell wählen, dann das gewählte Modell herunterladen. Erst danach ist Diktieren möglich.

### Voraussetzungen für Release-Downloads

- Windows 11 x64 und ein funktionierendes Mikrofon; in den Windows-Datenschutzeinstellungen den Mikrofonzugriff für Desktop-Apps erlauben.
- Setup-EXE und Portable-ZIP enthalten die .NET-Runtime, UI-Bibliotheken und native Spracherkennung. .NET SDK, Python oder CUDA müssen nicht installiert werden. Die Erkennung läuft auf der CPU; eine separate Grafikkarte ist nicht erforderlich.
- Für den ersten Modelldownload innerhalb von Murmur ist Internetzugriff auf GitHub nötig. Modellgewichte liegen nicht im Installer. Nach dem Download läuft die Spracherkennung lokal und ohne Internetverbindung; die Updateprüfung beim Start fragt weiterhin GitHub ab.
- Beim Portable-ZIP alle entpackten Dateien zusammenlassen. Die Anwendungs-EXE benötigt die Bibliotheken neben ihr.

Das .NET SDK im Abschnitt Build ist nur zum Kompilieren des Quellcodes nötig. Modelle werden mit dem in Windows 11 enthaltenen `tar.exe` entpackt; eine normale Windows-Installation benötigt kein zusätzliches Archivprogramm.

Murmur prüft beim Start auf eine neuere stabile Release. Bei einem Treffer öffnet ein Eintrag im Tray-Menü die zugehörige Release-Seite. Updates werden nicht selbst installiert.

Unterstütztes Beta-Ziel ist Windows 11 x64. Die beiden Modellarchive sind etwa
465 MiB (Parakeet) und 453 MiB (Nemotron) groß; die Modell-Dateien belegen in
der aktuellen lokalen Messung etwa 640 MiB bzw. 653 MiB. Download und Entpacken
benötigen zusätzlichen freien Speicher. Der RAM-Bedarf hängt von Modell,
Backend, Audiositzung und Rechner ab; eine garantierte RAM-Mindestanforderung oder Geschwindigkeit für sämtliche PCs wird nicht angegeben.

## Lokale Daten

Audio und Transkripte bleiben auf dem PC; sie werden nicht an einen Spracherkennungsdienst hochgeladen. Bei Neuinstallationen ist der Verlauf standardmäßig deaktiviert. Einstellungen, Verlauf, Wörterbuch und Logs sind lokal und unverschlüsselt; vor dem Teilen prüfen. Logs sind auf eine aktive Datei mit 2 MiB und zwei Sicherungen begrenzt.

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

Automatische Prüfungen können nicht jedes Mikrofon, jeden PC und jede Zielanwendung abdecken. Die Kompatibilität mit anderen Geräten wurde nicht umfassend geprüft.

## Lizenz

Der Murmur-Quellcode steht unter der MIT-Lizenz. Hinweise zu Modellen und gebündelten Drittanbieterkomponenten stehen in [NOTICE.md](NOTICE.md).

## Build

```powershell
cd windows
dotnet restore Murmur.sln
dotnet build Murmur.sln --configuration Release --no-incremental -warnaserror
dotnet test Murmur.sln --configuration Release --no-build
```

Normale CI nimmt Tests mit den Kategorien `RealModel` und `QualityCorpus` aus. Der Release-Workflow führt zusätzlich Modelldownload- und Inferenztests der Kategorie `RealModel` aus; der separate Workflow `.github/workflows/real-model.yml` prüft Modelle manuell oder nächtlich. Genauigkeitsmessungen der Kategorie `QualityCorpus` benötigen einen ausdrücklich bereitgestellten Korpus.

Weitere Details: [Windows-Einrichtung und Build-Hinweise](windows/README.md).
