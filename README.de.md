# Murmur

[English](README.md) | [Deutsch](README.de.md) | [Changelog](CHANGELOG.md)

Murmur ist ein Diktierprogramm für Windows. Push-to-Talk-Taste gedrückt halten, sprechen und loslassen. Murmur formatiert den Text und fügt ihn in die Anwendung mit Fokus ein.

Murmur ist eine eigenständige, KI-unterstützte Windows-Anpassung, inspiriert von [per-simmons/murmur-youtube](https://github.com/per-simmons/murmur-youtube).

## Download und erster Start

Murmur 0.2.0 wird als Beta-Release vorbereitet. Veröffentlichte Downloads stehen
auf der [GitHub-Releases-Seite](https://github.com/devjenjen/murmur-windows/releases):

- `Murmur-Setup-vX.Y.Z.exe` installiert Murmur für das aktuelle Benutzerkonto.
- `Murmur-<version>-win-x64.zip` ist portabel. Archiv entpacken und `Murmur.App.exe` starten; es wird nichts installiert.
- `build-info.json` enthält den Quellcode-Commit und Build-Angaben. `SHA256SUMS.txt` enthält die Prüfsummen der Downloads.

Der Installer ist nicht signiert. Windows SmartScreen kann deshalb eine Warnung anzeigen. Beim ersten Start öffnet Murmur die Einrichtung. Mikrofon, Taste und Sprachmodell wählen, dann das gewählte Modell herunterladen. Erst danach ist Diktieren möglich.

### Voraussetzungen für Release-Downloads

- Windows 11 x64 und ein funktionierendes Mikrofon; in den Windows-Datenschutzeinstellungen den Mikrofonzugriff für Desktop-Apps erlauben.
- Setup-EXE und Portable-ZIP enthalten die .NET-Runtime, UI-Bibliotheken und native Spracherkennung. .NET SDK, Python oder CUDA müssen nicht installiert werden. Die Erkennung läuft auf der CPU; eine separate Grafikkarte ist nicht erforderlich.
- Für den ersten Modelldownload innerhalb von Murmur ist Internetzugriff auf GitHub nötig. Modellgewichte liegen nicht im Installer. Nach dem Download läuft die Spracherkennung lokal und ohne Internetverbindung; die Updateprüfung beim Start fragt weiterhin GitHub ab.
- Beim Portable-ZIP alle entpackten Dateien zusammenlassen. Die Anwendungs-EXE benötigt die Bibliotheken neben ihr.

Das .NET SDK brauchst du nur beim Kompilieren des Quellcodes. Murmur entpackt Modelle mit dem in Windows 11 enthaltenen `tar.exe`.

Murmur prüft beim Start auf eine neuere stabile Release. Bei einem Treffer öffnet ein Eintrag im Tray-Menü die zugehörige Release-Seite. Updates werden nicht selbst installiert.

Die Modellarchive sind etwa 465 MiB (Parakeet) und 453 MiB (Nemotron) groß.
Entpackt belegen sie in der lokalen Release-Messung etwa 640 MiB und 653 MiB.
Plane zusätzlichen freien Speicher für Download und Entpacken ein. RAM-Bedarf
und Geschwindigkeit hängen vom Modell, der Audiositzung und dem PC ab;
garantierte Werte für jedes Gerät gibt es nicht.

## Lokale Daten

Audio und Transkripte bleiben auf dem PC; sie werden nicht an einen Spracherkennungsdienst hochgeladen. Bei Neuinstallationen ist der Verlauf standardmäßig deaktiviert. Einstellungen, Verlauf, Wörterbuch und Logs sind lokal und unverschlüsselt; vor dem Teilen prüfen. Logs sind auf eine aktive Datei mit 2 MiB und zwei Sicherungen begrenzt.

| Pfad | Inhalt |
|---|---|
| `%LOCALAPPDATA%\Murmur\settings.json` | Einstellungen |
| `%LOCALAPPDATA%\Murmur\history.jsonl` | Diktatverlauf |
| `%LOCALAPPDATA%\Murmur\dictionary.txt` | persönliches Wörterbuch |
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

Für einen Ton nach dem Einfügen kannst du eine eigene WAV-Datei unter
`Sounds\feedback.wav` neben `Murmur.App.exe` ablegen. Aktiviere dazu Audiofeedback
in den Einstellungen. Murmur liefert keine Sounddateien mit und bleibt ohne
diese Datei stumm.

## Grenzen

Die Erkennungsgenauigkeit wurde nicht gemessen. Die Auswahl des Standardmodells beruht auf keinem Genauigkeitsvergleich.

Automatische Prüfungen decken nur einen Teil des Verhaltens ab. Die Kompatibilität mit jedem Mikrofon, PC und Zielprogramm wurde nicht geprüft. Windows verhindert das Einfügen in Anwendungen mit höheren Rechten als Murmur.

## Lizenz

Der Murmur-Quellcode steht unter der MIT-Lizenz. Hinweise zu Modellen und gebündelten Drittanbieterkomponenten stehen in [NOTICE.md](NOTICE.md).

## Build

```powershell
cd windows
dotnet restore Murmur.sln
dotnet build Murmur.sln --configuration Release --no-incremental -warnaserror
dotnet test Murmur.sln --configuration Release --no-build --filter 'Category!=RealModel&Category!=QualityCorpus'
```

Dieser Befehl führt die Unit- und UI-Tests der normalen CI aus. Der
Release-Workflow lädt zusätzlich Modelle herunter und führt Inferenztests der
Kategorie `RealModel` aus. `.github/workflows/real-model.yml` prüft Modelle
manuell oder nächtlich. Tests der Kategorie `QualityCorpus` benötigen einen
separat bereitgestellten Korpus.

Weitere Details: [Windows-Einrichtung und Build-Hinweise](windows/README.md).
